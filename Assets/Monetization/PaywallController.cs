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

    private void RefreshOffering()
    {
        // Both labels are fully owned by this method from here on - never leave one showing a
        // stale state (e.g. a static "Loading..." placeholder) while the other has already
        // resolved to an error or success state.
        SetPrice("Loading...");
        SetStatus("");
        if (subscribeButton != null) subscribeButton.interactable = false;

        MonetizationManager.Instance.FetchOfferings((offerings, error) =>
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

        if (subscribeButton != null) subscribeButton.interactable = false;
        SetStatus("Processing...");

        MonetizationManager.Instance.Purchase(monthlyPackage, (success, error) =>
        {
            if (success)
            {
                SetStatus("Unlocked!");
                if (paywallRoot != null) paywallRoot.SetActive(false);
            }
            else
            {
                SetStatus(error ?? "Purchase failed.");
                if (subscribeButton != null) subscribeButton.interactable = true;
            }
        });
    }

    /// <summary>Wire to a "Restore Purchases" button.</summary>
    public void OnRestorePressed()
    {
        SetStatus("Restoring...");

        MonetizationManager.Instance.Restore((success, error) =>
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
