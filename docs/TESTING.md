# Doğrulama kayıtları ve kabul testleri

## Aşama 2 (geçiş ayarları) — 12.11.2026, master, yayımlanmadı

**Otomatik testler:** 52 test geçti; altı ardışık çalıştırmada da sonuç aynı. Uygulama sıfır uyarıyla derlendi. Yeni testler:

- **Kenar eşitleme:** DESKTOP'un dışında kalan x=100'den geçiş, DESKTOP'ta 75'e düşer; dönüş yine x=100'e iner.
- **Döndürme:** Soldaki monitörün sol kenarından çıkan imleç sağdaki monitörün sağ kenarına gelir. Orta monitörün alt kenarından çıkan imleç DESKTOP'a üstten girer.
- **Köşe engeli:** Köşede geçiş olmaz. İtme sürerken köşeden çıkılınca geçiş olur. Aynı itişte uyarı bir kez gösterilir.
- **Geçiş tuşu:** Tuş itme sırasında basılınca geçiş olur. Windows'un tuş durumuna bakılır. AltGr'nin ürettiği sahte Ctrl sayılmaz.
- **Tam ekran engeli ve çift dokunma:**
  - Ön pencere denetimi her itişte yalnızca bir kez yapılır.
  - 300 ms arayla iki dokunuş geçer; 600 ms arayla geçmez.
  - Kenar boyunca kaymak dokunuş sayılmaz.
  - Çift dokunma kapalıyken engel aşılmaz.
- **Her geçişte çift dokunma:** Kural iki yönde de geçerlidir. Karşı bilgisayardan dönüşte ön pencere denetimi yapılmaz.
- **Kısayollar:**
  - Bir kez tetiklenir; tuş tekrarı ve tuşun bırakılması yutulur.
  - Yerel sol Alt ile tetiklenince menü maskesi gönderilir.
  - AltGr+2 yazı olarak geçer.
  - Kayıt sırasında kısayollar çalışmaz.
  - Uzaktayken değiştirici tuşlar karşıya gider, kısayolun kendi tuşu gitmez.
- **Atlama ve sıra:**
  - Atlama ve "sonraki bilgisayar" döngüsü çalışır.
  - Monitör harfleri sırası: A = DESKTOP, B = sol, C = orta, D = sağ.
- **Ayar dosyası:**
  - 0.3 dosyası okunur ve gidiş-dönüşte bozulmaz; geçersiz değerler ayıklanır.
  - Kilitli dosyaya yazma, kilit kalkınca tamamlanır.
  - Arka plan yazıcısı yalnızca son değeri yazar.
- **İki denetleyici arasında, loopback TLS üzerinde:**
  - Ctrl+Alt+1 imleci DESKTOP'un ortasına, Ctrl+Alt+3 C'nin ortasına götürür.
  - DESKTOP, basılı Alt'ı bırakmadan önce menü maskesi gönderir.
  - Rakam tuşu DESKTOP'a sızmaz.
  - Engel uyarısı ve aç/kapat uyarısı doğru metinle gelir; ayar diske yazılır.

**Bulunan ve düzeltilen hata:** Ayar dosyasını o anda başka bir işlem açık tutarsa (test okuyucusu veya virüs tarayıcısı), dosyanın yerine yenisini koyma işlemi erişim reddiyle düşüyordu ve ayar kaydedilmiyordu. Test 5 çalıştırmanın 2'sinde bu yüzden başarısız oldu. Düzeltme:

- Okuyucular dosyayı silme paylaşımıyla açıyor.
- Yazma, erişim reddinde de yaklaşık 300 ms boyunca yeniden deneniyor.
- Bütün ayar yazımları (tercihler, düzen, eşleşmeler) arka plandaki tek bir yazıcıya taşındı. Fare kancasının beklediği kilit altında diske hiç yazılmıyor.

**Gerçek uygulama:**

- Geçiş ayarları sayfası ve yeni monitör harfleri ekranda doğru çiziliyor (ekran görüntüsüyle kontrol edildi).
- Kısayol kaydı gerçek klavyeyle denenmedi; o sırada bilgisayar başında çalışılıyordu ve tuş göndermek o işi bozabilirdi.

### İki cihaz kabul testi — Aşama 2

Her satır için yalnızca ilgili ayarı açın, sonra kapatın.

| Test | Beklenen |
| --- | --- |
| Eşitleme açık; C'nin üst kenarının en solundan ve en sağından it | Her ikisinde de A'ya geçer; A'nın alt kenarından dönünce aynı yerden çıkar |
| Döndürme açık; B'nin sol kenarından it | İmleç D'nin sağ kenarına gelir |
| Döndürme açık; C'nin alt kenarından it | İmleç A'ya üstten girer |
| Tam ekran engeli; C'de tam ekran video veya oyun açıkken yukarı it | Geçmez, durum çubuğu nedeni yazar; video B'de açıkken C'den geçiş çalışır |
| Büyütülmüş pencere engeli; C'de büyütülmüş pencere öndeyken it | Geçmez |
| Köşe engeli; C'nin sol üst köşesine yakın it (A eşitlemeyle oradaysa) | Geçmez; ortaya doğru kayınca geçer |
| Geçiş tuşu Shift; Shift'siz ve Shift'le it | Shift'siz geçmez, Shift'le geçer; A'da Shift takılı kalmaz |
| Program listesi (ör. notepad); Not Defteri öndeyken it | Geçmez |
| Engel varken çift dokunma | İki hızlı dokunuşla geçer; tek dokunuşla geçmez |
| Her geçişte çift dokunma | Gidişte ve dönüşte iki dokunuş gerekir |
| Kısayol kaydı: Ctrl+Shift+K'yi aç/kapat'a kaydet, sonra bas | Kutu "Ctrl+Shift+K" gösterir; basınca durum çubuğunda açıldı/kapatıldı yazar |
| Ctrl + sol Alt + 1 / 3 | İmleç A'nın / C'nin ortasına gider; A'da veya BANKOARTS'ta menü çubuğu açılmaz |
| AltGr+Q, AltGr+3, AltGr+7 (TR Q) atlama açıkken | @, #, { yazılır; atlama olmaz |
| Sonraki bilgisayar kısayolu iki kez | A'ya gider, sonra çıktığı yere döner |

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
