# Yeni sürüm yayımlama

Kaynak repo ve güncelleme kaynağı: `berkbankoglu/banko-share` (herkese açık). İstemciye token veya yayınlama yetkisi konulmaz.

1. `src/BankoShare.App/BankoShare.App.csproj` içindeki `Version` değerini artırın: ör. `0.2.0` → `0.2.1`.
2. `docs/RELEASE-NOTES.md` dosyasını yeni sürüme göre güncelleyin.
3. Değişiklikleri test edin, commit/push yapın.
4. Aynı sürümü etiketleyip gönderin:

```powershell
git tag v0.2.1
git push origin v0.2.1
```

`Publish Windows release` akışı test eder, self-contained Windows x64 uygulamasını derler, sabitlenmiş Velopack sürümüyle Setup/Portable/güncelleme paketini üretir. Tüm dosyalar taslak yayına yüklendikten sonra yayını görünür yapar. Yalnızca kaynak kodu push etmek kurulu uygulamaları güncellemez; sürüm etiketi gerekir.

Güncelleme kaynağında `releases.win.json` ve full `.nupkg` dosyaları mutlaka korunmalıdır. Setup dosyası tek başına güncelleme kanalı değildir. Yayınlanmış sürümün dosyalarını değiştirmeyin; düzeltme için sürümü artırın. Otomatik sürüm düşürme kapalıdır.

Yeni sürümler iki cihazda uygulama açıkken 10 dakika içinde (veya **Şimdi kontrol et** ile) algılanır. Aktif oturum bitene kadar indirilmiş sürüm bekletilir. Eski ZIP kullanıcılarının önce bir kez Setup sürümüne geçmesi gerekir.

## Güncelleme kabul testi

1. Önceki Setup sürümünü iki bilgisayara kurun.
2. Yeni sürümü yayımlayın. Bir cihazda oturum açık, diğerinde kapalı senaryolarını ayrı ayrı sınayın.
3. İki cihazın da yeni sürümü bulduğunu ve indirme durumunu gösterdiğini doğrulayın.
4. Aktif oturumun güncelleme nedeniyle kesilmediğini; oturum bitince geri sayım ve yeniden açılışı doğrulayın.
5. Sürüm etiketini ve otomatik güncelleme tercihinin korunduğunu kontrol edin.
6. Ağ yokken hata durumu görünmeli; mevcut uygulama kullanılabilmeli. Ağ geri gelince elle kontrol tekrar çalışmalı.

Windows Authenticode sertifikası henüz yoktur. Paket bütünlüğü Velopack ile doğrulanır; bu, yayıncının Authenticode imzasına sahip olduğu anlamına gelmez.
