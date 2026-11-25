package geoip

import "testing"

func TestRUPrefixes(t *testing.T) {
	db := RU()
	if n := db.Len(); n < 1000 {
		t.Fatalf("слишком мало диапазонов: %d", n)
	}
	t.Logf("диапазонов после склейки: %d", db.Len())

	ru := []string{
		"77.88.55.242",  // yandex.ru
		"87.250.250.242", // yandex
		"217.20.147.94",  // mail.ru
		"95.165.0.1",     // МГТС, Москва (подсеть)
		"178.248.232.209", // gosuslugi.ru
	}
	for _, ip := range ru {
		if !db.ContainsString(ip) {
			t.Errorf("ожидался RU-диапазон: %s", ip)
		}
	}

	foreign := []string{
		"8.8.8.8",         // Google DNS
		"1.1.1.1",         // Cloudflare
		"203.0.113.10",  // наш сервер, Испания
		"104.20.23.154",   // Cloudflare (example.com)
	}
	for _, ip := range foreign {
		if db.ContainsString(ip) {
			t.Errorf("не должен быть RU: %s", ip)
		}
	}
}

func TestBadInput(t *testing.T) {
	db := RU()
	if db.ContainsString("") || db.ContainsString("not-an-ip") {
		t.Fatal("мусор не должен матчиться")
	}
}
