namespace IonCrm.Application.Features.Leads;

/// <summary>
/// Scan order for the lead agent: Türkiye's 81 provinces, biggest elevator markets first. The API hands
/// the agent the least-recently scanned provinces so a weekly run sweeps the country in rotation.
/// </summary>
public static class LeadAgentCities
{
    public static readonly string[] All =
    {
        "İstanbul", "Ankara", "İzmir", "Bursa", "Antalya", "Kocaeli", "Konya", "Adana", "Gaziantep", "Mersin",
        "Kayseri", "Eskişehir", "Samsun", "Denizli", "Tekirdağ", "Sakarya", "Manisa", "Balıkesir", "Trabzon", "Muğla",
        "Aydın", "Diyarbakır", "Şanlıurfa", "Hatay", "Malatya", "Erzurum", "Kahramanmaraş", "Van", "Mardin", "Aksaray",
        "Afyonkarahisar", "Kütahya", "Çanakkale", "Edirne", "Kırklareli", "Yalova", "Bolu", "Düzce", "Zonguldak", "Karabük",
        "Bartın", "Kastamonu", "Çankırı", "Sinop", "Çorum", "Amasya", "Tokat", "Ordu", "Giresun", "Rize",
        "Artvin", "Gümüşhane", "Bayburt", "Erzincan", "Sivas", "Yozgat", "Kırşehir", "Nevşehir", "Niğde", "Karaman",
        "Isparta", "Burdur", "Uşak", "Bilecik", "Osmaniye", "Adıyaman", "Kilis", "Batman", "Siirt", "Şırnak",
        "Hakkari", "Bitlis", "Muş", "Bingöl", "Tunceli", "Elazığ", "Ağrı", "Iğdır", "Kars", "Ardahan", "Elâzığ",
    };

    /// <summary>Provinces not scanned within <paramref name="recentlyScanned"/>, in priority order.</summary>
    public static List<string> Next(int count, ISet<string> recentlyScanned)
    {
        var folded = recentlyScanned.Select(LeadNormalizer.Fold).ToHashSet();
        return All.Where(c => !folded.Contains(LeadNormalizer.Fold(c))).Distinct().Take(Math.Max(1, count)).ToList();
    }
}
