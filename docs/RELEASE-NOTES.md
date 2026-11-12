## Banko Share 0.4.1: donma düzeltmesi, arka planda çalışma ve yeni görünüm

0.4.0 kuruluysa bu güncelleme kendiliğinden gelir. Hemen almak için **Uygulama → Şimdi kontrol et** seçin.

### Düzeltmeler

- **Uzaktan kontrol edilen bilgisayarda donma giderildi.**
  - Belirti: Diğer bilgisayara geçip pencere sürükleyince ya da küçültünce imleç donabiliyordu ve o bilgisayarın kendi faresine dokunmak gerekiyordu.
  - Neden: Uygulama gelen hareketi Windows'a iletirken bir kilit tutuyordu. Windows ise hareketi işlemek için aynı uygulamanın fare kancasını bekliyordu. O sırada fiziksel fare kıpırdarsa iki taraf birbirini 1 saniyeye kadar bekliyordu. Bu tekrarlanırsa Windows kancayı kaldırabiliyordu.
  - Artık uzaktan gelen giriş ayrı bir iş parçacığında, hiçbir kilit tutulmadan uygulanıyor.

### Yeni

- **Bildirim alanında çalışma:** Pencereyi kapatmak uygulamayı kapatmaz. Banko Share saatin yanında çalışmaya ve paylaşmaya devam eder.
  - Simgeye tıklayınca pencere açılır.
  - Sağ tık menüsünde kenardan geçişi açma/kapama, bu bilgisayara dönme, Windows ile başlatma ve çıkış var.
  - Simge gri ise bağlı cihaz yok, yeşil ise bağlı. Köşesinde nokta varsa paylaşım sürüyor.
- **Windows ile başlatma:** Kurulu sürüm oturum açılınca bildirim alanında başlar. **Uygulama** sayfasından kapatılabilir.
- **Tek kopya:** Uygulama zaten açıkken yeniden açılırsa yeni kopya başlamaz, mevcut pencere öne gelir.
- **Yeni görünüm:** BankoSpace'in Noir tasarımı uygulandı (koyu yüzeyler, gri menü, yeşil vurgu, koyu başlık çubuğu). Uygulamanın yeni bir simgesi var.
- "Güncellemeler" sayfası **Uygulama** oldu; arka planda çalışma ve çıkış da bu sayfada.

Bu bir geliştirme önizlemesidir. Windows kod imzalama sertifikası henüz yoktur.
