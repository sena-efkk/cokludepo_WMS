namespace Wms.Modules.MasterData.Application.Import;

public static class SyntheticCatalogFactory
{
    public static IReadOnlyList<ProductCatalogItemInput> CreateCatalog()
    {
        var items = new List<ProductCatalogItemInput>();
        var index = 0;

        void Add(
            string product,
            string skuName,
            string brand,
            string category,
            string? uom = "EA",
            decimal? weightKg = null,
            decimal? lengthCm = null,
            decimal? widthCm = null,
            decimal? heightCm = null)
        {
            index++;
            items.Add(new ProductCatalogItemInput
            {
                Name = product,
                SkuName = skuName,
                Brand = brand,
                Category = category,
                Uom = uom,
                Barcode = $"999{1_000_000_000L + index}",
                WeightKg = weightKg,
                LengthCm = lengthCm,
                WidthCm = widthCm,
                HeightCm = heightCm,
            });
        }

        Add("Basic T-Shirt", "Siyah S", "Sentez Tekstil", "Tekstil", "EA", 0.18m, 25, 20, 2);
        Add("Basic T-Shirt", "Siyah M", "Sentez Tekstil", "Tekstil", "EA", 0.19m, 26, 21, 2);
        Add("Basic T-Shirt", "Siyah L", "Sentez Tekstil", "Tekstil", "EA", 0.20m, 27, 22, 2);
        Add("Basic T-Shirt", "Beyaz S", "Sentez Tekstil", "Tekstil", "EA", 0.18m, 25, 20, 2);
        Add("Basic T-Shirt", "Beyaz M", "Sentez Tekstil", "Tekstil", "EA", 0.19m, 26, 21, 2);
        Add("Cotton Hoodie", "Gri M", "Sentez Tekstil", "Tekstil", "EA", 0.55m, 40, 35, 5);
        Add("Cotton Hoodie", "Gri L", "Sentez Tekstil", "Tekstil", "EA", 0.60m, 42, 36, 5);
        Add("Denim Pantolon", "Mavi 32", "Sentez Tekstil", "Tekstil", "EA", 0.70m, 45, 30, 4);
        Add("Denim Pantolon", "Mavi 34", "Sentez Tekstil", "Tekstil", "EA", 0.72m, 45, 30, 4);

        Add("A4 Defter 96Y", "Çizgili", "MaviKalem", "Kırtasiye", "EA", 0.25m, 30, 21, 1);
        Add("A4 Defter 96Y", "Kareli", "MaviKalem", "Kırtasiye", "EA", 0.25m, 30, 21, 1);
        Add("Tükenmez Kalem", "Mavi", "MaviKalem", "Kırtasiye", "EA", 0.01m, 14, 1, 1);
        Add("Tükenmez Kalem", "Siyah", "MaviKalem", "Kırtasiye", "EA", 0.01m, 14, 1, 1);
        Add("Kurşun Kalem HB", "HB", "MaviKalem", "Kırtasiye", "EA", 0.01m, 18, 1, 1);
        Add("Ataş Kutusu", "100'lü", "MaviKalem", "Kırtasiye", "EA", 0.06m, 8, 6, 3);
        Add("Post-it 76x76", "Sarı", "MaviKalem", "Kırtasiye", "EA", 0.05m, 8, 8, 5);
        Add("A4 Kağıt 500'lü", "80gr", "MaviKalem", "Kırtasiye", "BOX", 2.5m, 30, 21, 5);

        Add("Mikrofiber Bez Seti", "3'lü", "EvKey", "Ev Yaşam", "EA", 0.30m, 35, 30, 8);
        Add("Mikrofiber Bez Seti", "5'li", "EvKey", "Ev Yaşam", "EA", 0.45m, 35, 30, 12);
        Add("Saklama Kabı", "1L", "EvKey", "Ev Yaşam", "EA", 0.12m, 15, 11, 8);
        Add("Saklama Kabı", "2L", "EvKey", "Ev Yaşam", "EA", 0.18m, 18, 13, 10);
        Add("Saklama Kabı", "3L", "EvKey", "Ev Yaşam", "EA", 0.25m, 21, 16, 12);
        Add("Ahşap Askı", "5'li", "EvKey", "Ev Yaşam", "EA", 0.50m, 45, 20, 8);
        Add("Ahşap Askı", "10'lu", "EvKey", "Ev Yaşam", "EA", 0.95m, 45, 20, 15);
        Add("LED Masa Lambası", "Beyaz Işık", "EvKey", "Ev Yaşam", "EA", 0.80m, 40, 15, 12);
        Add("Kedi Kumu", "5kg", "EvKey", "Ev Yaşam", "KG", 5.0m, 30, 20, 12);

        Add("Sıvı Sabun", "Lavanta 500ml", "LunaCare", "Kozmetik", "EA", 0.55m, 8, 5, 18);
        Add("Sıvı Sabun", "Deniz 500ml", "LunaCare", "Kozmetik", "EA", 0.55m, 8, 5, 18);
        Add("El Kremi 75ml", "Shea", "LunaCare", "Kozmetik", "EA", 0.09m, 5, 3, 13);
        Add("El Kremi 75ml", "Aloe", "LunaCare", "Kozmetik", "EA", 0.09m, 5, 3, 13);
        Add("Diş Fırçası", "Orta", "LunaCare", "Kozmetik", "EA", 0.02m, 19, 2, 2);

        Add("USB-C Kablo 1m", "Beyaz", "TeknoPlus", "Elektronik Aksesuar", "EA", 0.03m, 100, 1, 1);
        Add("USB-C Kablo 1m", "Siyah", "TeknoPlus", "Elektronik Aksesuar", "EA", 0.03m, 100, 1, 1);
        Add("Telefon Kılıfı", "Silikon Şeffaf", "TeknoPlus", "Elektronik Aksesuar", "EA", 0.04m, 16, 8, 2);
        Add("Kablosuz Mouse", "Siyah", "TeknoPlus", "Elektronik Aksesuar", "EA", 0.10m, 12, 7, 4);
        Add("Kulaklık Kılıfı", "Sert Kapak", "TeknoPlus", "Elektronik Aksesuar", "EA", 0.06m, 10, 8, 3);

        return items;
    }
}
