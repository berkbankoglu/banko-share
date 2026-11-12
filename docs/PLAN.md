# Banko Share yol haritası

Hedef: İki (veya daha fazla) Windows bilgisayarı tek fare ve klavyeyle, imleci ekran kenarından diğer bilgisayara iterek kullanmak. Monitör düzeni sürükle-bırak ile ayarlanır. Önce fare kusursuz çalışacak, diğer özellikler onun üzerine eklenecek.

Kurulum örneği (geliştirme ortamı):

```
              ┌───────────────┐
              │ A  DESKTOP-…  │
┌─────────────┼───────────────┴──┬─────────────┐
│ B           │ C  (ana ekran)    │ D           │
│ 1920×1080   │ 2560×1440         │ 1920×1080   │
└─────────────┴───────────────────┴─────────────┘
          BANKOARTS (3 monitör)
```

## Aşama 1 — Fare geçişi (v0.3.0, iki bilgisayarda sınanıyor)

Eski F12 ile elle açılan alıcı/gönderici modeli kalkar. Her bilgisayar aynı uygulamayı çalıştırır, hem kontrol eden hem kontrol edilen olabilir.

1. **Kalıcı cihaz kimliği ve tek seferlik eşleştirme**
   Her bilgisayarın Windows anahtar deposunda dışa aktarılamayan kalıcı bir anahtarı olur. 0.4.0'dan beri eşleştirme kodu 6 rakamdır ve her gösterimde yenilenir. Kod, iki sertifikaya bağlı bir SPAKE2 değişiminin parolasıdır; bir kez girilir ve 5 dakika geçerlidir. Eşleşen cihazlar hatırlanır, açılışta kendiliğinden bağlanır. Bağlantı karşılıklı TLS ile kurulur ve iki taraf da birbirinin sertifikasını sabitler.
2. **Ağda otomatik bulma**
   UDP yayınıyla aynı ağdaki Banko Share cihazları listelenir; IP adresi elle de girilebilir.
3. **Monitör bilgisi alışverişi**
   Her cihaz monitörlerini fiziksel piksel ve DPI ile gönderir. Monitör değişince bilgi güncellenir.
4. **Monitör düzeni ekranı**
   Bütün cihazların monitörleri tek tuvalde görünür. Bir cihazın monitör grubu sürüklenir ve kenarlara yapışır. Düzen iki bilgisayarda aynı kalacak şekilde eşitlenir.
5. **Kenardan geçiş**
   İmleç, düzende komşu monitör olan bir kenarı geçince diğer bilgisayara aktarılır ve aynı yoldan geri döner. Karşı tarafa mutlak konum gönderilir; böylece ivme iki kez uygulanmaz ve kayma olmaz. Bu sırada yerel imleç gizlenir.
6. **Akıcılık**
   Fare ve klavye kancaları arayüzden bağımsız, ayrı bir thread'de çalışır. Uygulama per-monitor DPI farkındalığıyla çalışır. Ağ yoğunlaştığında araya giren fare hareketleri birleştirilir, en güncel konum gönderilir.
7. **Güvenlik ağları**
   - Kontrol edilen bilgisayarın kendi faresi oynatılırsa kontrolü geri alır.
   - Bağlantı koparsa, ekran kilitlenirse veya bilgisayar uykuya geçerse imleç yerine döner.
   - Esc'e hızlıca iki kez basmak panik tuşudur.
   - Basılı kalan tuş ve düğmeler bırakılır.
   - Fare düğmesi basılıyken geçiş yapılmaz.
8. **Klavye fareyi takip eder**
   Klavye, imlecin bulunduğu bilgisayara gider. Geçişten önce basılan bir tuş bırakılınca yerelde bırakılır, takılı kalmaz.

**Kabul ölçütü:** İki bilgisayarda A↔C geçişi her yönde çalışmalı. 30 dakikalık kullanımda takılı tuş, kayıp tık, sıçrama veya kontrol kaybı olmamalı.

## Aşama 2 — Geçiş ayarları (v0.4.0, iki bilgisayarda sınanacak)

Hepsi varsayılan olarak kapalıdır; açılmadıkça 0.3.0 davranışı değişmez. Ayarlar her bilgisayarda ayrıdır ve o bilgisayarın faresi kullanılırken geçerlidir.

- **Ekran boyutlarını eşitle:** Kaynak monitörün kenarının tamamı, ona değen monitörlerin toplam kenarına orantılı eşlenir. Gidiş ve dönüş aynı noktaya düşer.
- **İmleci ekranların etrafında döndür:** Dış kenardan çıkan imleç, aynı satırın veya sütunun öbür ucundan girer. Bu, tek bilgisayarın kendi monitörleri arasında da çalışır.
- **Geçişi engelleme koşulları:**
  - Tam ekran uygulama veya büyütülmüş pencere öndeyken. Yalnızca imlecin çıktığı monitördeki pencere sayılır.
  - Ekran köşelerinde (96 DPI'da 40 piksel, DPI'ya göre ölçeklenir).
  - Shift, Ctrl veya sol Alt basılı değilken.
  - Listedeki programlar öndeyken.
  - Pencere koşulları bu bilgisayardan çıkarken denetlenir. Köşe, tuş ve çift dokunma kuralları her yönde geçerlidir.
- **Kenara çift dokunma:**
  - Engel varken iki dokunuşla geçilir. İsteğe bağlı olarak her geçiş iki dokunuş ister.
  - İki dokunuş arasında en fazla 500 ms olabilir.
  - İmleç kenardan en az 6 piksel geri çekilmelidir; kenar boyunca kaymak dokunuş sayılmaz.
- **Engel bildirimi:** Engellenen her itişte durum çubuğu nedenini yazar.
- **Kısayollar:**
  - Kenardan geçişi aç/kapat, sonraki bilgisayara geç ve bu bilgisayara dön kısayolları kullanıcı tarafından kaydedilir.
  - Ctrl + sol Alt + rakam veya Ctrl + Shift + rakam ile A–I monitörlerine atlanır. Harfler düzen ekranındakilerle aynıdır.
  - Kısayolda en az iki değiştirici tuş bulunur. AltGr sayılmaz; Türkçe Q'da @, #, {, [ gibi karakterler etkilenmez.
  - Alt ile tetiklenen kısayoldan sonra pencerenin menü çubuğu açılmaz.
  - Planlanan "düzen değiştir" kısayolunun yerini "sonraki bilgisayar" aldı. Birden fazla kayıtlı düzen ileride gerekirse ayrıca ele alınacak.

**Kabul ölçütü:** [TESTING.md](TESTING.md) içindeki Aşama 2 tablosu iki bilgisayarda geçmeli.

## Aşama 3 — Çıkış ayarları

- Dikey ve yatay kaydırma hızı, yönü ters çevirme.
- Uzak fare hızı.
- Fare takılı olmayan bilgisayarda imleç görünmüyorsa imleci uygulama çizer.
- Oyunlar için göreli fare modu.

## Aşama 4 — Veri alışverişi

- Pano eşitleme (önce metin, sonra görsel); uzak panoyu kısayolla yapıştırma.
- Bilgisayarlar arası dosya sürükle-bırak.
- Klavye dil düzenini eşitleme.
- Uzak olayları eşitleme: kapatma, uyku, kilit, ekran koruyucu, oturum açma.

## Aşama 5 — Ağ ve istemciler

- Ağ bağdaştırıcısı seçimi, TCP/UDP port ayarı, port çakışmasında boş port bulma, IPv6.
- İstemci listesi: elle ekleme ve silme, yerel IP'yi gösterme, kendi bağlantı bilgisini yayınlamayı açma/kapama.

## Aşama 6 — Diğer ve dağıtım

- Tepsi simgesi ve bildirimler, Windows ile başlatma.
- Panik tuşunu ayarlama.
- Pasif ekranları karartma, tanılama modu.
- Güncellemeler için proxy desteği.
- Authenticode imzası ve daha geniş uyumluluk testleri.

## Bilinen sınırlar

- Yönetici yetkisiyle çalışan pencereler, UAC, kilit ekranı ve Ctrl+Alt+Del için Windows normal kullanıcı uygulamasına izin vermez. Bunlar ayrı bir yetkili hizmet gerektirir; Aşama 6'dan sonra değerlendirilecek.
- Test sırasında ShareMouse iki bilgisayarda da kapalı olmalı. Aynı kenarı iki uygulama birden yakalarsa imleç iki yere birden geçmeye çalışır.
