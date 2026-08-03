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
            if (label != null) label.text = $"Resources: {amount}";
        }
    }
}
