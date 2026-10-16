namespace PMM_WeaponSkins;

internal static class Lang
{
	public static string Normalize(string? code)
	{
		return (code ?? "").Trim().ToLowerInvariant() switch
		{
			"ru" => "ru",
			"pl" => "pl",
			_ => "en"
		};
	}

	public static string Get(string lang, string key, params object[] args)
	{
		Dictionary<string, string> table = lang switch
		{
			"ru" => Ru,
			"pl" => Pl,
			_ => En
		};
		if (!table.TryGetValue(key, out string? text) && !En.TryGetValue(key, out text))
		{
			return key;
		}
		for (int i = 0; i < args.Length; i++)
		{
			text = text.Replace("{" + i + "}", args[i]?.ToString() ?? "", StringComparison.Ordinal);
		}
		return text;
	}

	private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
	{
		["back"] = "‹   BACK",
		["equipped"] = "Equipped",
		["default"] = "Default",
		["none"] = "None",
		["cat.weapons"] = "WEAPONS",
		["cat.knife"] = "KNIFE",
		["cat.gloves"] = "GLOVES",
		["cat.agents"] = "AGENTS",
		["cat.music"] = "MUSIC",
		["cat.pins"] = "PINS",
		["cat.stickers"] = "STICKERS",
		["sub.weapons"] = "Skins for every weapon",
		["sub.stickers"] = "For the held weapon",
		["crumb.root"] = "Your loadout",
		["crumb.sep"] = "  ›  ",
		["prompt.title"] = "Type in the chat",
		["prompt.hint"] = "Press Y, type the answer and press Enter. Type  -  to go back.",
		["hint.items"] = "{0} items  ·  click a card to select",
		["hint.item"] = "1 item  ·  click a card to select",
		["hint.empty"] = "Nothing to show here",
		["hint.board"] = "Click a row to change it",
		["equipped.count"] = "{0} equipped",
		["on"] = "On",
		["off"] = "Off",
		["line.wear"] = "Wear  {0}",
		["line.seed"] = "Pattern  {0}",
		["line.stattrak"] = "StatTrak  {0}",
		["line.nametag"] = "Name tag  {0}",
		["wear.fn"] = "Factory New",
		["wear.mw"] = "Minimal Wear",
		["wear.ft"] = "Field-Tested",
		["wear.ww"] = "Well-Worn",
		["wear.bs"] = "Battle-Scarred",
		["rarity.1"] = "Consumer Grade",
		["rarity.2"] = "Industrial Grade",
		["rarity.3"] = "Mil-Spec",
		["rarity.4"] = "Restricted",
		["rarity.5"] = "Classified",
		["rarity.6"] = "Covert",
		["rarity.7"] = "Contraband",
		["rarity.glove"] = "Extraordinary",
		["rarity.item.1"] = "Base Grade",
		["rarity.item.3"] = "High Grade",
		["rarity.item.4"] = "Remarkable",
		["rarity.item.5"] = "Exotic",
		["rarity.item.6"] = "Extraordinary",
		["rarity.item.7"] = "Contraband",
		["rarity.agent.3"] = "Distinguished",
		["rarity.agent.4"] = "Exceptional",
		["rarity.agent.5"] = "Superior",
		["rarity.agent.6"] = "Master",
		["slots.used"] = "{0} / 4 stickers",
		["row.all"] = "All",
		["row.search"] = "Search",
		["row.remove"] = "Remove",
		["row.remove_slot"] = "Remove Slot {0}",
		["row.remove_all"] = "Remove All"
	};

	private static readonly Dictionary<string, string> Ru = new(StringComparer.Ordinal)
	{
		["back"] = "‹   НАЗАД",
		["equipped"] = "Надето",
		["default"] = "По умолчанию",
		["none"] = "Нет",
		["cat.weapons"] = "ОРУЖИЕ",
		["cat.knife"] = "НОЖ",
		["cat.gloves"] = "ПЕРЧАТКИ",
		["cat.agents"] = "АГЕНТЫ",
		["cat.music"] = "МУЗЫКА",
		["cat.pins"] = "ЗНАЧКИ",
		["cat.stickers"] = "НАКЛЕЙКИ",
		["sub.weapons"] = "Скины для всего оружия",
		["sub.stickers"] = "Для оружия в руках",
		["crumb.root"] = "Ваше снаряжение",
		["prompt.title"] = "Введите в чат",
		["prompt.hint"] = "Нажмите Y, введите ответ и нажмите Enter. Введите  -  чтобы вернуться.",
		["hint.items"] = "Предметов: {0}  ·  нажмите на карточку",
		["hint.item"] = "Предметов: 1  ·  нажмите на карточку",
		["hint.empty"] = "Здесь пока ничего нет",
		["hint.board"] = "Нажмите на строку, чтобы изменить",
		["equipped.count"] = "Надето: {0}",
		["on"] = "Вкл",
		["off"] = "Выкл",
		["line.wear"] = "Износ  {0}",
		["line.seed"] = "Узор  {0}",
		["line.stattrak"] = "StatTrak  {0}",
		["line.nametag"] = "Имя  {0}",
		["wear.fn"] = "Прямо с завода",
		["wear.mw"] = "Немного поношенное",
		["wear.ft"] = "После полевых испытаний",
		["wear.ww"] = "Поношенное",
		["wear.bs"] = "Закалённое в боях",
		["rarity.1"] = "Ширпотреб",
		["rarity.2"] = "Промышленное",
		["rarity.3"] = "Армейское",
		["rarity.4"] = "Запрещённое",
		["rarity.5"] = "Засекреченное",
		["rarity.6"] = "Тайное",
		["rarity.7"] = "Контрабанда",
		["rarity.glove"] = "Экстраординарное",
		["rarity.item.6"] = "Экстраординарное",
		["rarity.item.7"] = "Контрабанда",
		["slots.used"] = "Наклеек: {0} / 4",
		["row.all"] = "Все",
		["row.search"] = "Поиск",
		["row.remove"] = "Удалить",
		["row.remove_slot"] = "Удалить слот {0}",
		["row.remove_all"] = "Удалить все"
	};

	private static readonly Dictionary<string, string> Pl = new(StringComparer.Ordinal)
	{
		["back"] = "‹   WSTECZ",
		["equipped"] = "Założone",
		["default"] = "Domyślne",
		["none"] = "Brak",
		["cat.weapons"] = "BROŃ",
		["cat.knife"] = "NÓŻ",
		["cat.gloves"] = "RĘKAWICE",
		["cat.agents"] = "AGENCI",
		["cat.music"] = "MUZYKA",
		["cat.pins"] = "PRZYPINKI",
		["cat.stickers"] = "NAKLEJKI",
		["sub.weapons"] = "Skórki do całej broni",
		["sub.stickers"] = "Dla trzymanej broni",
		["crumb.root"] = "Twój ekwipunek",
		["prompt.title"] = "Wpisz na czacie",
		["prompt.hint"] = "Naciśnij Y, wpisz odpowiedź i naciśnij Enter. Wpisz  -  aby wrócić.",
		["hint.items"] = "Przedmiotów: {0}  ·  kliknij kartę",
		["hint.item"] = "Przedmiotów: 1  ·  kliknij kartę",
		["hint.empty"] = "Nic tu nie ma",
		["hint.board"] = "Kliknij wiersz, aby go zmienić",
		["equipped.count"] = "Założone: {0}",
		["on"] = "Wł.",
		["off"] = "Wył.",
		["line.wear"] = "Zużycie  {0}",
		["line.seed"] = "Wzór  {0}",
		["line.stattrak"] = "StatTrak  {0}",
		["line.nametag"] = "Nazwa  {0}",
		["wear.fn"] = "Prosto z fabryki",
		["wear.mw"] = "Niewielkie zużycie",
		["wear.ft"] = "Po testach w terenie",
		["wear.ww"] = "Znoszone",
		["wear.bs"] = "Zniszczone",
		["rarity.1"] = "Konsumencka",
		["rarity.2"] = "Przemysłowa",
		["rarity.3"] = "Wojskowa",
		["rarity.4"] = "Ograniczona",
		["rarity.5"] = "Tajne",
		["rarity.6"] = "Ukryta",
		["rarity.7"] = "Kontrabanda",
		["rarity.glove"] = "Niezwykła",
		["rarity.item.6"] = "Niezwykła",
		["rarity.item.7"] = "Kontrabanda",
		["slots.used"] = "Naklejek: {0} / 4",
		["row.all"] = "Wszystkie",
		["row.search"] = "Szukaj",
		["row.remove"] = "Usuń",
		["row.remove_slot"] = "Usuń slot {0}",
		["row.remove_all"] = "Usuń wszystkie"
	};
}
