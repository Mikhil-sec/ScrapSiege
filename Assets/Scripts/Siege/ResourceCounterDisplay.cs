using TMPro;
using UnityEngine;

namespace ScrapSiege.Siege
{
    /// <summary>Thin UI-formatting wrapper so ResourceEconomy doesn't need to know about TMP.</summary>
    public class ResourceCounterDisplay : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;

        /// <summary>Wire to ResourceEconomy.OnResourceCountChanged.</summary>
        public void SetCount(int amount)
        {
            // Bare number by design - the HUD chip this sits in already carries a "SCRAP" caption,
            // so repeating the word here would just make the counter wider on a phone screen.
            if (label != null) label.text = amount.ToString();
        }
    }
}
