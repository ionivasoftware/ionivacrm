namespace IonCrm.Domain.Enums;

/// <summary>
/// Quality/potential label for a customer record.
/// Maps to the Turkish business concepts used in ION CRM.
/// </summary>
public enum CustomerLabel
{
    /// <summary>High potential — top priority lead or customer.</summary>
    YuksekPotansiyel = 1,

    /// <summary>Potential — moderate interest or opportunity.</summary>
    Potansiyel = 2,

    /// <summary>Neutral — no clear signal in either direction.</summary>
    Notr = 3,

    /// <summary>Below average — low engagement or value.</summary>
    Vasat = 4,

    /// <summary>Poor — very low engagement or negative experience.</summary>
    Kotu = 5
}
