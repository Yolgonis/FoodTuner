# FoodTuner 1.0.0

Valheim'daki yiyeceklerin **süresini** ve verdiği **buffları** (can, dayanıklılık, eitr, can yenileme) ve mead/iksirlerin **etki süresi, bekleme süresi ve gücünü** ayarlayan **client-side** mod. Sunucuda veya diğer oyuncularda kurulu olması gerekmez; değişiklikler yalnızca senin karakterini etkiler.

## Kullanım

Oyunda **F7** ile paneli aç (tuş config'den değiştirilebilir).

- **Genel çarpanlar**: tüm yiyeceklere uygulanır. `2` = iki katı, `0,5` = yarısı, `1` = orijinal.
  - Yemek süresi, Can, Dayanıklılık, Eitr, Can yenileme
  - İksir süresi: mead etkisinin süresi *ve* tekrar içme beklemesi
  - İksir gücü: yenileme bonusları (ör. %50 → %100) ve zamanla verilen can/dayanıklılık/eitr
- **Eşya bazlı ayar**: soldan bir yiyecek/iksir seç, "Özel değer" kutusuna sayı yaz, **Uygula ve kaydet**. Boş bırakılan alanlar genel çarpanı kullanır. ★ işaretli eşyalarda özel ayar vardır.
- **Etkin** kutusunu kapatınca her şey anında orijinal değerlere döner.
- Süre uzatılınca **o an aktif olan yemeklerin** kalan süresi de aynı oranda uzar.

Liste satırı: `Can/Dayanıklılık/Eitr  süre (dk:sn)`.

## Dosyalar

- `BepInEx/config/local.foodtuner.cfg` — tuş, ölçek, genel çarpanlar (ConfigurationManager ile de düzenlenebilir; anında uygulanır).
- `BepInEx/config/FoodTuner.overrides.txt` — eşya bazlı ayarlar. Elle de düzenlenebilir, sonra panelde **Dosyadan yeniden yükle**:

```
CookedMeat: burn=3600, health=50, regen=4
MeadHealthMinor: ttl=60, hpOverTime=100
```

Anahtarlar: `burn` (süre, sn), `health`, `stamina`, `eitr`, `regen`, `ttl` (iksir süresi/bekleme, sn), `hpRegen`/`stRegen`/`eitrRegen` (yenileme çarpanı, 1.5 = +%50), `hpOverTime`/`stOverTime`/`eitrOverTime`. Dosyada ondalık için **nokta** kullan (`40.5`); virgül alan ayırıcıdır. Panelde virgül de kabul edilir.

## Kurulum

`BepInEx/plugins/FoodTuner/FoodTuner.dll` dosyasını profilindeki aynı yola kopyala. Sadece BepInEx gerekir.

## Notlar ve sınırlar

- Değerler oyun içi tooltip'te de görünür (yeni değerleri okur). Mead tooltip'indeki açıklama metni oyunun sabit metnidir, güncellenmez; "Şu an" sütunu gerçek değeri gösterir.
- Aynı durum efektini paylaşan iki iksir varsa son uygulanan ayar ikisine de geçer.
- **WackysDatabase** gibi eşya değiştiren modlarla uyumludur: başka bir mod değeri değiştirirse o değer yeni "orijinal" kabul edilir; FoodTuner çarpanları onun üstüne uygular. Liste dünyaya girişte ve panel her açıldığında yenilenir.
- Kaydedilmiş karakter verisine bir şey yazılmaz; modu kaldırınca her şey orijinale döner.
- Sunucu tarafında yemek değerlerini zorlayan bir mod varsa (ServerSync ile) onunla çakışabilir.

## Derleme

```powershell
dotnet build FoodTuner.csproj -c Release
dotnet run --project tests/RulesChecks/RulesChecks.csproj -c Release
```

`GamePath` / `ProfilePath` csproj'da yerel varsayılanlara ayarlı; gerekirse `-p:GamePath=...` ile değiştir.
