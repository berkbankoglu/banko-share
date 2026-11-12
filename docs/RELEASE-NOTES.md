## Banko Share 0.4.0: 6 haneli eşleştirme kodu ve geçiş ayarları

**İki bilgisayar da 0.4.0 olmalı.** Eşleştirme yöntemi değişti; 0.3.0 ile 0.4.0 birbiriyle eşleşemez. 0.3.0 kuruluysa güncelleme kendiliğinden gelir. Beklemek istemezseniz **Güncellemeler → Şimdi kontrol et** seçin. İlk kurulum için **BankoShare-win-Setup.exe** dosyasını kullanın.

### Yeni

- **6 haneli eşleştirme kodu:** "Eşleştirme kodu göster" ve "Yeni kod" düğmeleri her basışta yeni bir kod üretir. Kod 5 dakika geçerlidir ve bir kez kullanılır.
  - Kod ağda hiç gönderilmez. SPAKE2 yöntemiyle iki taraf aynı kodu bildiğini, kodu açık etmeden kanıtlar.
  - Ağdaki biri kodu deneme-yanılmayla çözemez. Her bağlantı denemesi yalnızca tek bir tahmine izin verir ve 5 yanlış denemede kod iptal olur.
  - Araya girip iki bilgisayar arasında aracılık eden biri doğru kodu iletse bile eşleştirme başarısız olur.
- **Geçiş ayarları:** Hepsi kapalı başlar; açılmadıkça 0.3.0 davranışı değişmez.
  - **Ekran boyutlarını eşitle:** Kenarın her yerinden, orantılı geçiş.
  - **İmleci ekranların etrafında döndür.**
  - **Geçişi engelle:** Tam ekran veya büyütülmüş pencere öndeyken, ekran köşelerinde, Shift/Ctrl/sol Alt basılı değilken veya listedeki programlar öndeyken geçiş yapılmaz. Engellenince durum çubuğu nedeni yazar.
  - **Kenara çift dokunma:** Engel varken iki dokunuşla geçilir. İsterseniz her geçiş iki dokunuş ister.
  - **Kısayollar:** Geçişi aç/kapat, sonraki bilgisayar, bu bilgisayara dön ve Ctrl + sol Alt (ya da Ctrl + Shift) + rakam ile monitöre atlama. AltGr kısayol sayılmaz; @, # gibi karakterler etkilenmez.

### Düzeltmeler

- Ayar dosyası başka bir program tarafından (örneğin virüs tarayıcısı) kısa süre açık tutulduğunda değişiklik kaydedilmiyordu. Artık kayıt yeniden deneniyor ve arka planda yapılıyor; fare hiçbir zaman diske yazılmasını beklemiyor.

### Kurulum

1. İki bilgisayarda da ShareMouse gibi başka bir paylaşım uygulaması açıksa kapatın.
2. Windows güvenlik duvarı sorarsa izin verin.
3. Bir bilgisayarda **Eşleştirme kodu göster** seçin. Diğerinde listeden onu seçip 6 rakamı girin ve **Eşleştir** seçin.
4. **Monitör düzeni** sayfasında yerleşimi ayarlayın.

Bu bir geliştirme önizlemesidir. Windows kod imzalama sertifikası henüz yoktur.
