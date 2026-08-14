using System;
using UnityEngine;
using ScrapSiege.Monetization;

/// <summary>
/// Owns the RevenueCat SDK lifecycle: configuration, offerings/purchase/restore calls, and
/// pushing entitlement state into ProEntitlement for the rest of the game to read. Deliberately
/// placed outside Assets/Scripts (and so outside ScrapSiege.Runtime.asmdef) because the
/// RevenueCat SDK ships with no .asmdef of its own and compiles into Unity's implicit default
/// assembly - only that default assembly can reference a named one, not the other way round, so
/// anything that touches the Purchases API directly has to live here too. See ProEntitlement.cs
/// for why gameplay code never needs to reference this class or the SDK.
///
/// Single persistent instance (DontDestroyOnLoad) so entitlement state survives the
/// Fortify -> Siege scene/state transition.
///
/// Runs from Start(), not Awake(), and forces a later execution order: Purchases.Start()
/// (default order 0) is what actually allocates its internal wrapper, so calling
/// purchases.Configure() before that has run - e.g. from Awake(), which runs for every
/// object in the scene before any Start() does - throws a NullReferenceException. This bit
/// on-device, not in the Editor, since [RequireComponent] only guarantees the Purchases
/// component exists, not that its own lifecycle methods have already run.
/// </summary>
[RequireComponent(typeof(Purchases))]
[DefaultExecutionOrder(100)]
public class MonetizationManager : MonoBehaviour
{
    /// <summary>Must match the entitlement identifier ("lookup_key") configured in the RevenueCat dashboard.</summary>
    public const string ProEntitlementId = "pro";

    [Tooltip("RevenueCat public API key for this app (RevenueCat dashboard > Project > Apps > API Keys). Safe to store here - it's a public/client key, not a secret.")]
    [SerializeField] private string revenueCatApiKey;

    public static MonetizationManager Instance { get; private set; }

    private Purchases purchases;

    private void Start()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        purchases = GetComponent<Purchases>();

        if (string.IsNullOrEmpty(revenueCatApiKey))
        {
            Debug.LogError("MonetizationManager: RevenueCat API key is not set - purchases will not work.", this);
            return;
        }

        if (!purchases.useRuntimeSetup)
        {
            // Too late to fix at runtime: Purchases.Start() already ran before this Start()
            // (that ordering is the whole point of [DefaultExecutionOrder] above), so if
            // useRuntimeSetup was still false, its own auto-configure-with-empty-keys attempt
            // has already fired this frame. This has to be set on the serialized component
            // (Inspector checkbox) before Play/build, not corrected here.
            Debug.LogError("MonetizationManager: Purchases.useRuntimeSetup is false - it must be checked in the Inspector, or Purchases.Start() will already have tried (and failed) to auto-configure itself with empty API key fields before this ran.", this);
        }

        // AutoSyncPurchases must be set explicitly. Configure() bypasses the Inspector fields
        // entirely (they only feed Purchases.Start()'s own auto-configure path, which
        // useRuntimeSetup turns off), and PurchasesConfiguration.Builder.Build() defaults the
        // whole DangerousSettings block to `new DangerousSettings(false)` - i.e. auto-sync OFF -
        // when none was supplied. That is the opposite of the component's own default of true, and
        // the SDK announces it on every launch with "Automatic syncing of purchases has been
        // disabled". With it off, RevenueCat never observes a Play purchase it did not itself
        // initiate, so any purchase whose receipt POST failed at the time (network drop, or a
        // backend rejection) is never retried and the entitlement stays dark forever.
        var config = Purchases.PurchasesConfiguration.Builder.Init(revenueCatApiKey)
            .SetDangerousSettings(new Purchases.DangerousSettings(true))
            .Build();
        purchases.Configure(config);

        RefreshCustomerInfo();
    }

    /// <summary>
    /// Re-reads entitlement state whenever the app comes back to the foreground.
    ///
    /// The Google Play purchase flow runs in its own activity (ProxyBillingActivity) on top of
    /// ours, so every purchase is bracketed by a pause/resume - as is subscribing, cancelling or
    /// restoring from the Play Store app itself. Without this the only entitlement reads are at
    /// Start() and in the purchase callback, which means a subscription that changed while the app
    /// was backgrounded is invisible until the next cold start.
    /// </summary>
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus || purchases == null || Instance != this) return;
        RefreshCustomerInfo();
    }

    [Tooltip("Minimum seconds between two customer-info fetches. Focus changes drive refreshes, and " +
             "an app can be focused and unfocused as fast as the OS can switch windows.")]
    [SerializeField] private float minRefreshIntervalSeconds = 20f;

    private float lastRefreshTime = float.NegativeInfinity;
    private bool refreshInFlight;

    /// <summary>
    /// True when a customer-info fetch may be issued now. Cheap client-side rate limiting.
    ///
    /// <para><b>What this is actually defending against.</b> Not fraud - a purchase cannot be forged
    /// by calling this, because entitlements are decided by RevenueCat from a Google-signed receipt
    /// and never by anything the client says. What it defends against is <i>volume</i>: every
    /// entitlement read is bracketed by an app focus change, and an automated focus/unfocus loop (or
    /// a device stuck cycling one) would fire an unbounded number of backend calls under this
    /// project's own API key. The failure mode is rate limiting that lands on real players trying to
    /// restore a subscription, so the throttle is here rather than being left to the backend.</para>
    ///
    /// <para>Correctness is unaffected: RevenueCat's SDK caches customer info for minutes anyway,
    /// and a genuine entitlement change is delivered through the purchase/restore callbacks, which
    /// are NOT throttled - only the speculative background refresh is.</para>
    /// </summary>
    private bool MayRefresh()
    {
        if (refreshInFlight) return false;
        return Time.unscaledTime - lastRefreshTime >= Mathf.Max(0f, minRefreshIntervalSeconds);
    }

    /// <summary>
    /// Pushes any purchases Google Play knows about but RevenueCat does not up to the backend, then
    /// applies whatever comes back. This is the recovery path for a purchase that completed on the
    /// store but whose receipt RevenueCat rejected at the time - most commonly because the Play
    /// Store service credentials were missing or still propagating, which surfaces in the logs as
    /// "PurchasesError(code=InvalidCredentialsError, ... Invalid Play Store credentials)".
    /// Nothing about that state is fixable in the client; once the dashboard side is correct, this
    /// is what makes an already-owned subscription register without a reinstall.
    /// </summary>
    public void SyncPurchases(Action<bool, string> onComplete = null)
    {
        if (!TryBeginStoreOperation(out string busy))
        {
            onComplete?.Invoke(false, busy);
            return;
        }

        purchases.SyncPurchases((customerInfo, error) =>
        {
            storeOperationInFlight = false;

            if (error != null)
            {
                Debug.LogWarning($"MonetizationManager: SyncPurchases failed - {error.Message}");
                onComplete?.Invoke(false, error.Message);
                return;
            }
            ApplyCustomerInfo(customerInfo);
            onComplete?.Invoke(true, null);
        });
    }

    private bool storeOperationInFlight;

    /// <summary>
    /// One store operation at a time. Purchase, restore and sync all mutate the same entitlement
    /// state and all go out over the network, so overlapping them means racing callbacks writing
    /// <see cref="ProEntitlement"/> in an order nobody chose.
    ///
    /// <para><b>The concrete case this closes.</b> The paywall's Subscribe button disables itself
    /// while a purchase is in flight, but Restore did not, so a held finger (or an automated tapper,
    /// or an accessibility repeat) could issue an unbounded stream of RestorePurchases calls.
    /// Enforcing it here rather than only in the UI means a second screen wired to these methods
    /// later cannot reintroduce it, and the guard sits next to the calls it is guarding.</para>
    /// </summary>
    private bool TryBeginStoreOperation(out string busyMessage)
    {
        if (storeOperationInFlight)
        {
            busyMessage = "Already working on it...";
            return false;
        }

        storeOperationInFlight = true;
        busyMessage = null;
        return true;
    }

    /// <summary>
    /// Re-reads entitlement state. Rate limited - see <see cref="MayRefresh"/>. Pass
    /// <paramref name="force"/> only for a user-initiated action, never for a background refresh.
    /// </summary>
    public void RefreshCustomerInfo(bool force = false)
    {
        if (!force && !MayRefresh()) return;

        lastRefreshTime = Time.unscaledTime;
        refreshInFlight = true;

        purchases.GetCustomerInfo((customerInfo, error) =>
        {
            refreshInFlight = false;

            if (error != null)
            {
                Debug.LogWarning($"MonetizationManager: failed to fetch customer info - {error.Message}");
                return;
            }
            ApplyCustomerInfo(customerInfo);
        });
    }

    [Tooltip("Minimum seconds between two offerings fetches. The paywall is a panel that gets " +
             "toggled, so it refetches every time it opens.")]
    [SerializeField] private float minOfferingsIntervalSeconds = 10f;

    private float lastOfferingsTime = float.NegativeInfinity;
    private Purchases.Offerings cachedOfferings;

    /// <summary>
    /// Fetches the current offerings, serving a recent result from memory rather than re-asking.
    ///
    /// <para>Prices do not change between two taps, and the paywall can be opened and closed as fast
    /// as a finger moves - so without this, "open paywall, close, repeat" is a free unbounded stream
    /// of backend calls. The callback contract is unchanged, so callers cannot tell which path they
    /// got; only the first fetch after the interval reaches the network.</para>
    /// </summary>
    public void FetchOfferings(Purchases.GetOfferingsFunc callback)
    {
        if (cachedOfferings != null
            && Time.unscaledTime - lastOfferingsTime < Mathf.Max(0f, minOfferingsIntervalSeconds))
        {
            callback?.Invoke(cachedOfferings, null);
            return;
        }

        purchases.GetOfferings((offerings, error) =>
        {
            if (error == null && offerings != null)
            {
                cachedOfferings = offerings;
                lastOfferingsTime = Time.unscaledTime;
            }

            callback?.Invoke(offerings, error);
        });
    }

    /// <summary>
    /// RevenueCat's own name for what went wrong ("ProductAlreadyPurchasedError",
    /// "InvalidCredentialsError", ...). Compared by string rather than by the numeric code because
    /// the SDK exposes it as a string and the numbers are not part of its documented surface.
    /// </summary>
    public const string ProductAlreadyPurchasedErrorCode = "ProductAlreadyPurchasedError";

    /// <summary>
    /// <paramref name="onComplete"/> receives (success, message, readableErrorCode). The error code
    /// is surfaced separately from the message because callers need to branch on it - notably on
    /// <see cref="ProductAlreadyPurchasedErrorCode"/>, which means Google Play already has the
    /// subscription and buying again is impossible; the only way forward there is a sync/restore.
    /// </summary>
    public void Purchase(Purchases.Package package, Action<bool, string, string> onComplete)
    {
        if (!TryBeginStoreOperation(out string busy))
        {
            onComplete?.Invoke(false, busy, null);
            return;
        }

        purchases.PurchasePackage(package, (productIdentifier, customerInfo, userCancelled, error) =>
        {
            storeOperationInFlight = false;

            if (error != null)
            {
                onComplete?.Invoke(false, error.Message, error.ReadableErrorCode);
                return;
            }
            if (userCancelled)
            {
                onComplete?.Invoke(false, "Cancelled", null);
                return;
            }
            ApplyCustomerInfo(customerInfo);
            onComplete?.Invoke(true, null, null);
        });
    }

    public void Restore(Action<bool, string> onComplete)
    {
        if (!TryBeginStoreOperation(out string busy))
        {
            onComplete?.Invoke(false, busy);
            return;
        }

        purchases.RestorePurchases((customerInfo, error) =>
        {
            storeOperationInFlight = false;

            if (error != null)
            {
                onComplete?.Invoke(false, error.Message);
                return;
            }
            ApplyCustomerInfo(customerInfo);
            onComplete?.Invoke(true, null);
        });
    }

    private void ApplyCustomerInfo(Purchases.CustomerInfo customerInfo)
    {
        bool unlocked = customerInfo.Entitlements.Active.ContainsKey(ProEntitlementId);
        ProEntitlement.SetUnlocked(unlocked);
    }
}
