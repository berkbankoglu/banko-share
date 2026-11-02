# İki cihaz kabul testi

## Güncelleme doğrulaması — 02.11.2026

- 15 otomatik protokol/güncelleme koordinasyonu testi geçti.
- `v0.2.0` GitHub Actions derleme ve yayın akışları başarılı.
- `dotnet run --project tests/BankoShare.Tests -c Release -- --release-smoke 0.2.0` başarılı: herkese açık sürüm keşfi, hesapsız indirme ve hash doğrulama, güncel sürümü tekrar sunmama, sürüm düşürmeyi engelleme ve bozuk paket reddi.
- Yayınlanan uygulamanın bir kopyası test için `0.1.99` paket sürümüyle ayrı bir portable klasöre paketlendi. Uygulamanın GitHub'dan `0.2.0` paketini alıp uyguladığı doğrulandı; `sq.version` dosyası `0.2.0` oldu ve yeniden açılan arayüz **Güncel · v0.2.0** gösterdi.
- Bu test yerel bir portable paket üzerinde yapıldı; iki fiziksel bilgisayardaki kurulum ve fare/klavye kabul testlerinin yerine geçmez.

## Fare ve klavye kabul testi

Durum: henüz uygulanmadı. Her iki cihazda fiziksel klavye/fare bulunsun. İlk denemeyi boş bir metin düzenleyicide yapın.

| Test | Beklenen |
| --- | --- |
| Alıcı kapalıyken bağlan | Zaman aşımı/hata; yerel kontrol korunur |
| Yanlış eşleştirme kodu | Bağlantı reddedilir; giriş uygulanmaz |
| Doğru kod | Bağlı fakat yerel kontrol açık |
| F12 | Uzak fare ve klavye aktif |
| Fare hareketi, sol/sağ/orta/yan düğmeler, iki tekerlek ekseni | Doğru hareket ve düğme eşleşmesi |
| TR Q harfleri, Shift, AltGr, Ctrl+C/V, Alt+Tab | Hedef klavye düzenine uygun giriş |
| F12 ile geri dönüş | Yerel kontrol hemen geri gelir; uzakta tüm tuş/düğmeler bırakılır |
| Ctrl veya fare düğmesi basılıyken ağı kes | Alıcı yaklaşık 3 saniyede bırakır; gönderici yerel kontrole döner |
| Alıcıda fiziksel F12 | Oturum kapanır; tekrar eşleştirme gerekir |
| Göndericiyi kapat | Alıcı kopmayı algılar ve basılı girişleri bırakır |
| Uygulama odağını kaybet | Gönderici uzak kontrolü kapatır |
| Ekran kilitle / uyku / uyanma | Oturum güvenli biçimde kapanır; yeni bağlantı gerekir |
| %100 / %150 DPI, çift monitör | Hareket doğruluğu ve hız farkları kaydedilir |
| Yönetici penceresi | Desteklenmediği anlaşılır; uygulama çökmez |
| Yüksek polling rate fare / ağ yoğunluğu | Kuyruk taşarsa oturum kapanır; tuş kaybı sessizce geçiştirilmez |

Başarı koşulu: iki cihazda en az 30 dakikalık normal kullanımda takılı tuş, kayıp tıklama ve kontrol kaybı olmaması. Bu sonuç kaydedilmeden günlük kullanıma hazır etiketi verilmez.
