# Nederlandse OpenResto-installatie (ontwikkelbranch)

Deze branch voegt Nederlands toe als taal in de frontend en backend. Het merendeel van de gastreserveringsschermen is vertaald; delen van het beheerscherm en extra modules gebruiken voorlopig nog Engelse teksten. Dit is **geen vrijgave voor productie** totdat de CI en praktijktests zijn geslaagd.

## Versie en taal

- Git-branch: `feature/dutch-language`
- Voor de productiecompose: zet `OPENRESTO_DEFAULT_LOCALE=nl` in de lokale `.env` van OpenResto.
- Bestaande installaties blijven standaard Engels als deze variabele ontbreekt.
- Kies Nederlands vanuit het taalmenu; eerdere taalkeuzes van gasten blijven lokaal bewaard en hebben voorrang op de ingestelde standaardtaal.
- Reserverings- en restaurantmeldingen via e-mail gebruiken bij standaardtaal `nl` Nederlandse teksten en 24-uurs tijd.
- Vertalingen zijn geen juridisch getoetste privacyverklaring: controleer privacyteksten, bewaartermijnen en privacyverklaring voor livegang.

## Installatie op Klip (voorbereiding)

Gebruik de officiële OpenResto self-hosting-instructies, maar installeer vanuit deze fork en branch. De bestaande Docker- en reverseproxy-configuratie op Klip is leidend; overschrijf geen bestaande poorten, Nginx-configuratie, databases of websites. De VPS-compose publiceert lokaal poort 8080 en 8443: **eerst controleren of die vrij zijn**. De ontwikkelcompose publiceert poort 5062, en is niet bedoeld voor productie.

1. Controleer de gezondheid van de gekozen SSD, schijfruimte en actieve containers. Gebruik de eerder defect bevonden WALRAM-SSD niet.
2. Maak een back-up en leg vast welke reverseproxy en domeinen nu actief zijn.
3. Check deze branch uit, maak de benodigde lokale `.env`/secrets conform de upstream-documentatie, en stel `OPENRESTO_DEFAULT_LOCALE=nl` in.
4. Bouw met `docker compose -f docker-compose.vps.yml build` **pas na poort- en configuratiecontrole**.
5. Test de webinterface, API, Nederlandse reserveringsbevestigingen en SMTP met eigen testgegevens.
6. Controleer herstellen van de databaseback-up voordat echte gastreserveringen binnenkomen.

## Open punten

- De rest van de beheerschermen en aanvullende modules vertalen, bij voorkeur met context in de daadwerkelijke UI.
- Alle reserverings- en annuleringstrajecten testen, inclusief e-mails met speciale tekens en allergie-informatie.
- Bevestigen dat CI-tests en Docker-build slagen; niet aannemen dat toegevoegde tests ook geslaagd zijn.
- Controleer workflow-compatibiliteit en verschillen met upstream vóór het teruggeven van de Nederlandse vertaling.
