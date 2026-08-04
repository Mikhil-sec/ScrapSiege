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

        var config = Purchases.PurchasesConfiguration.Builder.Init(revenueCatApiKey).Build();
        purchases.Configure(config);

        RefreshCustomerInfo();
    }

    public void RefreshCustomerInfo()
    {
        purchases.GetCustomerInfo((customerInfo, error) =>
        {
            if (error != null)
            {
                Debug.LogWarning($"MonetizationManager: failed to fetch customer info - {error.Message}");
                return;
            }
            ApplyCustomerInfo(customerInfo);
        });
    }

    public void FetchOfferings(Purchases.GetOfferingsFunc callback)
    {
        purchases.GetOfferings(callback);
    }

    public void Purchase(Purchases.Package package, Action<bool, string> onComplete)
    {
        purchases.PurchasePackage(package, (productIdentifier, customerInfo, userCancelled, error) =>
        {
            if (error != null)
            {
                onComplete?.Invoke(false, error.Message);
                return;
            }
            if (userCancelled)
            {
                onComplete?.Invoke(false, "Cancelled");
                return;
            }
            ApplyCustomerInfo(customerInfo);
            onComplete?.Invoke(true, null);
        });
    }

    public void Restore(Action<bool, string> onComplete)
    {
        purchases.RestorePurchases((customerInfo, error) =>
        {
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
