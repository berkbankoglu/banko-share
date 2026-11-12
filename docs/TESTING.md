# Doğrulama kayıtları ve kabul testleri

## Aşama 1 (kenardan geçiş) — 12.11.2026, BANKOARTS üzerinde

**Otomatik testler:** 39 test geçti; üç ardışık çalıştırmada da sonuç aynı. Uygulama sıfır uyarıyla derlendi. Kapsam:

- Protokol ve eşleştirme kodu.
- Düzen yerleşimi, çakışma çözümü ve kenara yapıştırma.
- Gerçek geliştirme düzeninde geçiş motoru: DESKTOP, 2560×1440 orta monitörün üstünde.
- Loopback TLS ile eşleştirme, yanlış kod ve sertifika, yeniden bağlanma, sessiz eşin kesilmesi ve çift bağlantı ayıklama.
- İki denetleyici arasında uçtan uca geçiş, yazma, geri alma ve eş kaybı.

**Kanca koordinatı ölçümü:** Sol kenar −1920'deyken imleç sola itildi. Düşük seviye kanca −1933 ve −1940 bildirdi; gerçek imleç −1920'de kaldı. Sağ kenarda da aynı sonuç alındı (4479 → 4497). Yani kanca noktası kırpılmaz ve geçiş tespiti bu davranışa dayanır.

**Gerçek uygulama, kontrol edilen taraf:**

- Bir test eşi, çalışan uygulamayla loopback üzerinden gerçek kodla eşleşti.
- Uygulama monitörleri doğru bildirdi: (−1920,0) 1920×1080, (0,0) 2560×1440 ana ekran, (2560,0) 1920×1080; hepsi 96 DPI.
- Test eşi üç monitörün iç noktalarına ve köşe piksellerine mutlak konum gönderdi. **Dokuz noktanın dokuzu tam pikselinde doğrulandı;** gönderimden imlecin varışına kadar 1–6 ms geçti.
- Test sırasında bilgisayar başındaki kullanıcı fareyi oynattı. Uygulama kontrolü geri aldı ve test eşine geri alma mesajı gönderdi. Uygulamanın kendi gönderdiği girişler geri almayı tetiklemedi.
- Test kayıtları sonradan ayarlardan silindi.

**Henüz doğrulanmayanlar (ikinci bilgisayar gerekiyor):**

- Kontrol eden taraf: kenara itince geçiş, yerel imlecin gizlenmesi, geri dönüş.
- İki gerçek bilgisayar arasında ağda bulma, eşleştirme ve düzen eşitleme.

## İki cihaz kabul testi — Aşama 1

Kurulum: BANKOARTS (3 monitör) ve DESKTOP-7EAHGJM (A, orta monitörün üstünde). İki bilgisayarda ShareMouse kapalı, Banko Share Setup ile kurulu. İlk denemeyi boş bir metin düzenleyicide yapın.

| Test | Beklenen |
| --- | --- |
| Ağda bulma | Her iki tarafın "Ağda bulunan" listesinde diğeri görünür |
| Yanlış kod / başka cihazın kodu | Reddedilir; 5 yanlış denemeden sonra kod iptal olur |
| Doğru kod | İki tarafta da "Bağlı"; monitörler düzende görünür |
| A'yı C'nin üstüne sürükle | Kenara yapışır; diğer bilgisayarın düzeni de aynı olur |
| C'nin üst kenarından A'nın altında kalan bölüme it | İmleç A'ya, x konumu korunarak geçer; BANKOARTS imleci görünmez |
| C'nin üst kenarından A'nın dışında kalan bölüme it | Geçiş olmaz |
| A'da gezin, alt kenardan dön | İmleç C'de aynı x'ten çıkar |
| A'da sol, sağ, orta ve yan düğmeler; iki tekerlek ekseni | Doğru eşleşme |
| A'da TR Q harfleri, Shift, AltGr, Ctrl+C/V, Alt+Tab, Win | Hedef düzene uygun giriş; BANKOARTS'ta tetiklenmez |
| Ctrl basılıyken A'ya geç, Ctrl'ü bırak | BANKOARTS'ta Ctrl takılı kalmaz |
| Pencere sürüklerken kenara git | Geçiş olmaz |
| A'dayken Esc, Esc | İmleç C'de çıktığı noktaya döner |
| A'dayken DESKTOP'un kendi faresini oynat | Kontrol DESKTOP'a geçer, BANKOARTS imleci geri gelir |
| A'dayken ağ kablosunu çek | ~3 sn içinde BANKOARTS imleci geri gelir; A'da basılı tuşlar bırakılır |
| Uygulamayı bir tarafta kapat / aç | Diğer taraf kopmayı görür; açılınca kendiliğinden yeniden bağlanır |
| Ekran kilitle / uyku / uyanma | İmleç eve döner; sonra geçiş yeniden çalışır |
| DESKTOP'tan BANKOARTS'a ters yönde geçiş | Aynı davranış |
| Hızlı hareket, yüksek polling rate fare | Takılma veya sıçrama olmaz |
| Yönetici penceresi | Giriş uygulanamaz uyarısı; uygulama çökmez |

Başarı koşulu: İki cihazda en az 30 dakikalık normal kullanımda takılı tuş, kayıp tık, sıçrama veya kontrol kaybı olmaması. Bu sonuç kaydedilmeden günlük kullanıma hazır etiketi verilmez.

## Önceki kayıtlar

### Güncelleme doğrulaması — 02.11.2026

- `v0.2.0` GitHub Actions derleme ve yayın akışları başarılıydı. `--release-smoke 0.2.0` ile herkese açık sürüm keşfi, hesapsız indirme, hash doğrulama, sürüm düşürme engeli ve bozuk paket reddi doğrulandı.
- `0.1.99` olarak paketlenmiş bir kopya, GitHub'dan `0.2.0`'ı alıp uyguladı ve **Güncel · v0.2.0** gösterdi.
