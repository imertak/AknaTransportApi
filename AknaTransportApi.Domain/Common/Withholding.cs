namespace AknaTransportApi.Domain.Common;

/// <summary>KDV tevkifatı türü. <see cref="Rate"/> KDV'nin yüzde kaçının alıcı tarafından kesileceğidir (2/10 = 20).</summary>
public sealed record WithholdingType(string Code, int Rate, string Name)
{
    /// <summary>Kesir gösterimi: 20 → "2/10".</summary>
    public string Fraction => $"{Rate / 10}/10";
}

/// <summary>
/// GİB KDV kısmi tevkifat kodları (KDV Genel Uygulama Tebliği I/C-2.1.3). e-Arşiv'de vergi kodu 9015
/// (KDV Tevkifat) ile gönderilir. Nakliye için tipik kod 624 (Yük Taşımacılığı, 2/10).
/// </summary>
public static class WithholdingTypes
{
    public const string TaxCode = "9015";

    public static readonly IReadOnlyList<WithholdingType> All =
    [
        new("624", 20, "Yük taşımacılığı hizmeti"),
        new("614", 50, "Servis taşımacılığı hizmeti"),
        new("616", 50, "Diğer hizmetler"),
        new("601", 40, "Yapım işleri ile bu işlerle birlikte ifa edilen mühendislik-mimarlık ve etüt-proje hizmetleri"),
        new("602", 90, "Etüt, plan-proje, danışmanlık, denetim ve benzeri hizmetler"),
        new("603", 70, "Makine, teçhizat, demirbaş ve taşıtlara ait tadil, bakım ve onarım hizmetleri"),
        new("604", 50, "Yemek servis hizmeti"),
        new("605", 50, "Organizasyon hizmeti"),
        new("606", 90, "İşgücü temin hizmetleri"),
        new("607", 90, "Özel güvenlik hizmeti"),
        new("608", 90, "Yapı denetim hizmetleri"),
        new("609", 70, "Fason tekstil ve konfeksiyon, çanta ve ayakkabı dikim işleri ve aracılık hizmetleri"),
        new("610", 90, "Turistik mağazalara verilen müşteri bulma / götürme hizmetleri"),
        new("611", 90, "Spor kulüplerinin yayın, reklam ve isim hakkı gelirleri"),
        new("612", 90, "Temizlik hizmeti"),
        new("613", 90, "Çevre, bahçe ve bakım hizmetleri"),
        new("615", 70, "Her türlü baskı ve basım hizmetleri"),
        new("617", 70, "Hurda metalden elde edilen külçe teslimleri"),
        new("618", 70, "Bakır, çinko, demir çelik, alüminyum ve kurşun külçe teslimi"),
        new("619", 70, "Bakır, çinko ve alüminyum ürünlerinin teslimi"),
        new("620", 70, "İstisnadan vazgeçenlerin hurda ve atık teslimi"),
        new("621", 90, "Hurda ve atıklardan elde edilen hammadde teslimi"),
        new("622", 90, "Pamuk, tiftik, yün ve yapağı ile ham post ve deri teslimleri"),
        new("623", 50, "Ağaç ve orman ürünleri teslimi"),
        new("625", 30, "Ticari reklam hizmetleri"),
        new("626", 20, "Diğer teslimler"),
        new("627", 50, "Demir-çelik ürünlerinin teslimi"),
    ];

    public static WithholdingType? Find(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : All.FirstOrDefault(w => w.Code == code.Trim());
}
