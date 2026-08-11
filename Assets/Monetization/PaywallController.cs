using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimal paywall: one package (the "default" offering's $rc_monthly), a price label, a
/// Subscribe button, and a Restore button. Lives alongside MonetizationManager outside
/// ScrapSiege.Runtime.asmdef for the same reason - it touches RevenueCat's Package/Offerings
/// types directly.
/// </summary>
public class PaywallController : MonoBehaviour
{
    [SerializeField] private TMP_Text priceLabel;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private Button subscribeButton;
    [SerializeField] private Button restoreButton;
    [SerializeField] private GameObject paywallRoot;

    private Purchases.Package monthlyPackage;

    private void OnEnable()
    {
        RefreshOffering();
    }

    /// <summary>
    /// MonetizationManager configures RevenueCat from Start() at execution order 100, so it is not
    /// available during another object's Awake/OnEnable on the first frame of a scene - and it is
    /// absent entirely from any scene that does not carry it. Every entry point checks rather than
    /// dereferencing Instance directly, because the failure mode otherwise is a NullReferenceException
    /// inside a UI callback, which surfaces as a dead button rather than as an obvious error.
    /// </summary>
    private bool TryGetManager(out MonetizationManager manager)
    {
        manager = MonetizationManager.Instance;
        if (manager != null) return true;

        SetPrice("--");
        SetStatus("Store unavailable.");
        if (subscribeButton != null) subscribeButton.interactable = false;
        Debug.LogError("PaywallController: no MonetizationManager in the scene (or it has not started yet) - the paywall cannot reach RevenueCat.", this);
        return false;
    }

    private void RefreshOffering()
    {
        // Both labels are fully owned by this method from here on - never leave one showing a
        // stale state (e.g. a static "Loading..." placeholder) while the other has already
        // resolved to an error or success state.
        SetPrice("Loading...");
        SetStatus("");
        if (subscribeButton != null) subscribeButton.interactable = false;

        if (!TryGetManager(out var manager)) return;

        manager.FetchOfferings((offerings, error) =>
        {
            if (error != null)
            {
                SetPrice("--");
                SetStatus("Offer unavailable right now.");
                Debug.LogError($"PaywallController: GetOfferings failed - {error.Message}");
                return;
            }

            monthlyPackage = offerings.Current?.Monthly;
            if (monthlyPackage == null)
            {
                SetPrice("--");
                SetStatus("No offer configured.");
                return;
            }

            SetPrice($"{monthlyPackage.StoreProduct.PriceString} / month");
            SetStatus("");
            if (subscribeButton != null) subscribeButton.interactable = true;
        });
    }

    /// <summary>Wire to a "Subscribe" button.</summary>
    public void OnSubscribePressed()
    {
        if (monthlyPackage == null) return;
        if (!TryGetManager(out var manager)) return;

        if (subscribeButton != null) subscribeButton.interactable = false;
        SetStatus("Processing...");

        manager.Purchase(monthlyPackage, (success, error, errorCode) =>
        {
            if (success)
            {
                SetStatus("Unlocked!");
                if (paywallRoot != null) paywallRoot.SetActive(false);
                return;
            }

            // Google Play refuses a second purchase of a subscription the account already owns
            // (ITEM_ALREADY_OWNED). Reaching this means the store and RevenueCat disagree: the
            // player is paying but has no entitlement, and the raw error - "This product is
            // already active for the user" - reads as a taunt. Nothing the player can do fixes
            // it, so recover on their behalf by pushing the store's own record up to RevenueCat.
            if (errorCode == MonetizationManager.ProductAlreadyPurchasedErrorCode)
            {
                SetStatus("Already subscribed - restoring...");
                manager.SyncPurchases((syncOk, syncError) =>
                {
                    if (syncOk && ScrapSiege.Monetization.ProEntitlement.IsUnlocked)
                    {
                        SetStatus("Unlocked!");
                        if (paywallRoot != null) paywallRoot.SetActive(false);
                        return;
                    }

                    // Sync "succeeded" but the entitlement is still dark, or it failed outright.
                    // Either way the break is on the store/dashboard side, not in this session.
                    SetStatus("Subscription active on Google Play but not verified yet. Try again shortly.");
                    if (subscribeButton != null) subscribeButton.interactable = true;
                });
                return;
            }

            SetStatus(error ?? "Purchase failed.");
            if (subscribeButton != null) subscribeButton.interactable = true;
        });
    }

    /// <summary>Wire to a "Restore Purchases" button.</summary>
    public void OnRestorePressed()
    {
        if (!TryGetManager(out var manager)) return;

        SetStatus("Restoring...");

        manager.Restore((success, error) =>
        {
            SetStatus(success ? "Restored." : (error ?? "Restore failed."));
        });
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null) statusLabel.text = message;
    }

    private void SetPrice(string message)
    {
        if (priceLabel != null) priceLabel.text = message;
    }
}
