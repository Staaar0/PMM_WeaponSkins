using System.Globalization;

namespace PMM_WeaponSkins;

internal static class Layout
{
	public const int CardCount = 40;
	public const int CardsPerRow = 5;
	public const int WindowRows = 3;
	public const int RowCount = 10;
	public const int PagerButtons = 7;
	public const int StickerSlots = 4;
	public const int PreviewLines = 4;

	public static readonly string[] Rail = { "weapons", "knife", "gloves", "agents", "music", "pins", "stickers" };
}

internal static class Kind
{
	public const string Categories = "categories";
	public const string Weapons = "weapons";
	public const string Skins = "skins";
	public const string Customize = "customize";
	public const string Wear = "wear";
	public const string NameTag = "nametag";
	public const string Seed = "seed";
	public const string Knives = "knives";
	public const string Gloves = "gloves";
	public const string GloveSkins = "glove-skins";
	public const string GloveCustomize = "glove-customize";
	public const string GloveWear = "glove-wear";
	public const string GloveSeed = "glove-seed";
	public const string ApplyTo = "apply-to";
	public const string AgentTeams = "agent-teams";
	public const string AgentFactions = "agent-factions";
	public const string Agents = "agents";
	public const string Music = "music";
	public const string PinGroups = "pin-groups";
	public const string Pins = "pins";
	public const string StickerSlots = "sticker-slots";
	public const string StickerRemove = "sticker-remove";
	public const string StickerSources = "sticker-sources";
	public const string StickerEvents = "sticker-events";
	public const string StickerTypes = "sticker-types";
	public const string StickerAudience = "sticker-audience";
	public const string Stickers = "stickers";
	public const string StickerSearch = "sticker-search";
	public const string Loading = "loading";
}

internal interface ICatalog
{
	string? WeaponName(int def);

	string? PaintName(int def, int paint);

	int FirstPaint(int def);

	List<int> CategoryWeapons(string category);

	string? AgentName(string model);

	string? MusicName(int id);

	string? PinName(int id);

	string? FirstAgent(string faction);

	bool IsGlove(int def);

	int FirstPin(string group);

	IReadOnlyList<int> StickerIds(string source, string? group, string? type, string? audience);

	int EventSticker(string source, string? group, string? type, Func<int, bool>? hasArt = null);
}

internal sealed class Frame
{
	public required MenuSnapshot Snap;
	public required LoadoutSnapshot Loadout;
	public required bool Ct;
	public required string Language;
	public required ItemIndex Items;
	public required ICatalog Catalog;
	public Func<int, bool> Disabled = _ => false;

	public TeamSnapshot Team => Loadout.Team(Ct);

	public string T(string key, params object[] args) => Lang.Get(Language, key, args);
}

internal sealed class Card
{
	public string Name = "";
	public string Sub = "";
	public string? Art;
	public string? Base;
	public int Rarity;
	public bool Stock;
	public bool EqT;
	public bool EqCt;
	public bool Disabled;

	public bool Plain;
}

internal sealed class Row
{
	public string Name = "";
	public string Value = "";
	public string? Art;
	public bool? On;
	public bool Eq;
	public bool Disabled;

	public bool Center;
}

internal sealed class Preview
{
	public string Title = "";
	public string Sub = "";
	public string? Art;
	public string? Base;
	public int Rarity;
	public readonly string[] Lines = new string[Layout.PreviewLines];
	public readonly int[] Stickers = new int[Layout.StickerSlots];
	public bool ShowStickers;

	public bool Live;

	public bool All;

	public Preview()
	{
		Array.Fill(Lines, "");
	}
}

internal sealed class RailInfo
{
	public string Sub = "";
	public string? Art;
}

internal static class CardFactory
{
	private static readonly HashSet<string> BoardKinds = new(StringComparer.Ordinal)
	{
		Kind.Customize, Kind.Wear, Kind.NameTag, Kind.Seed, Kind.GloveCustomize, Kind.GloveWear, Kind.GloveSeed,
		Kind.ApplyTo, Kind.StickerSlots, Kind.StickerRemove
	};

	private static readonly HashSet<string> StickerPreviewKinds = new(StringComparer.Ordinal)
	{
		Kind.Customize, Kind.Wear, Kind.NameTag, Kind.Seed, Kind.StickerSlots, Kind.StickerRemove
	};

	public static bool SortsByRarity(string kind)
	{
		return kind is Kind.Skins or Kind.GloveSkins or Kind.Agents or Kind.Music or Kind.Pins or Kind.Stickers or Kind.StickerSearch;
	}

	private const int Pinned = 100;

	private static int SortRarity(Frame f, string? tag)
	{
		Split(tag, out string verb, out string arg);
		switch (f.Snap.Kind)
		{
			case Kind.Skins when verb == "paint":
				return f.Items.Skin(f.Snap.WeaponDef, Int(arg));
			case Kind.GloveSkins when verb == "paint":
				return f.Items.Skin(f.Snap.GloveDef, Int(arg));
			case Kind.Agents when verb == "agent":
				return f.Items.Agent(arg);
			case Kind.Music when verb == "music":
				return Math.Max(3, f.Items.Music(Int(arg)));
			case Kind.Pins when verb == "pin":
				return f.Items.Pin(Int(arg));
			case Kind.Stickers or Kind.StickerSearch when verb == "sticker":
				return f.Items.Sticker(Int(arg));
			default:
				return Pinned;
		}
	}

	public static int[]? GridOrder(Frame f)
	{
		MenuSnapshot snap = f.Snap;
		if (!snap.Typed || snap.Count < 2 || !SortsByRarity(snap.Kind))
		{
			return null;
		}
		MenuInfo? info = snap.Info;
		int key = snap.WeaponDef * 31 + snap.GloveDef;
		if (info is { OrderDone: true } && info.OrderKey == key)
		{
			return info.Order;
		}
		int[] rarity = new int[snap.Count];
		for (int i = 0; i < rarity.Length; i++)
		{
			rarity[i] = SortRarity(f, snap.Tag(i));
		}
		int[] order = Enumerable.Range(0, rarity.Length).OrderByDescending(i => rarity[i]).ToArray();
		if (info != null)
		{
			info.Order = order;
			info.OrderKey = key;
			info.OrderDone = true;
		}
		return order;
	}

	public static int[] BoardRows(Frame f)
	{
		List<int> rows = new();
		int count = Math.Min(f.Snap.Count, Layout.RowCount);
		for (int i = 0; i < count; i++)
		{
			if (f.Snap.Kind == Kind.StickerSlots)
			{
				Split(f.Snap.Tag(i), out string verb, out _);
				if (verb is "slot" or "slot-all")
				{
					continue;
				}
			}
			rows.Add(i);
		}
		return rows.ToArray();
	}

	public static int IndexOfTag(MenuSnapshot snap, string tag)
	{
		for (int i = 0; i < snap.Count; i++)
		{
			if (string.Equals(snap.Tag(i), tag, StringComparison.Ordinal))
			{
				return i;
			}
		}
		return -1;
	}

	public static bool IsBoard(MenuSnapshot snap) => snap.Typed && snap.Count <= Layout.RowCount && BoardKinds.Contains(snap.Kind);

	public static bool IsKnife(int def) => def is 42 or 59 || (def >= 500 && def < 600);

	public static bool IsGloveFallback(int def) => def >= 5000 || def == 4725;

	public static string RailKey(string rootKind)
	{
		return rootKind switch
		{
			Kind.Knives => "knife",
			Kind.Gloves or Kind.GloveSkins or Kind.GloveCustomize or Kind.GloveWear or Kind.GloveSeed => "gloves",
			Kind.AgentTeams or Kind.AgentFactions or Kind.Agents => "agents",
			Kind.Music => "music",
			Kind.PinGroups or Kind.Pins => "pins",
			Kind.StickerSlots or Kind.StickerRemove or Kind.StickerSources or Kind.StickerEvents or Kind.StickerTypes
				or Kind.StickerAudience or Kind.Stickers or Kind.StickerSearch => "stickers",
			_ => "weapons"
		};
	}


	private static void Split(string? tag, out string verb, out string arg)
	{
		tag ??= "";
		int colon = tag.IndexOf(':');
		if (colon < 0)
		{
			verb = tag;
			arg = "";
		}
		else
		{
			verb = tag[..colon];
			arg = tag[(colon + 1)..];
		}
	}

	private static int Int(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

	private static int PaintOn(TeamSnapshot team, int def) => team.Weapon(def)?.Paint ?? 0;

	private static string StripId(string text, string id)
	{
		string suffix = " (" + id + ")";
		return id.Length > 0 && text.EndsWith(suffix, StringComparison.Ordinal) ? text[..^suffix.Length] : text;
	}

	private static string RarityLabel(Frame f, int def, int rarity)
	{
		if (rarity <= 0)
		{
			return "";
		}
		if (f.Catalog.IsGlove(def) && rarity == 6)
		{
			return "★ " + f.T("rarity.glove");
		}
		return (IsKnife(def) ? "★ " : "") + f.T("rarity." + rarity);
	}

	private static string ItemRarity(Frame f, string family, int rarity)
	{
		string key = "rarity." + family + "." + rarity;
		string text = f.T(key);
		return text == key ? f.T("rarity." + rarity) : text;
	}


	public static Card MakeCard(Frame f, int index)
	{
		ItemSnapshot item = f.Snap.Item(index);
		Split(item.Tag, out string verb, out string arg);
		Card card = new() { Name = item.Text.Trim(), Disabled = f.Disabled(index) };
		switch (f.Snap.Kind)
		{
			case Kind.Categories:
				Category(f, card, arg);
				break;
			case Kind.Weapons:
				Weapon(f, card, verb, Int(arg));
				break;
			case Kind.Skins:
				Skin(f, card, verb, arg);
				break;
			case Kind.Knives:
				KnifeCard(f, card, verb, Int(arg));
				break;
			case Kind.Gloves:
				GloveCard(f, card, verb, Int(arg));
				break;
			case Kind.GloveSkins:
				GloveSkin(f, card, arg);
				break;
			case Kind.AgentTeams:
				card.Art = Art.DefaultAgent(arg == "ct");
				break;
			case Kind.AgentFactions:
				Faction(f, card, verb, arg);
				break;
			case Kind.Agents:
				AgentCard(f, card, arg);
				break;
			case Kind.Music:
				MusicCard(f, card, verb, Int(arg));
				break;
			case Kind.PinGroups:
				PinGroup(f, card, verb, arg);
				break;
			case Kind.Pins:
				PinCard(f, card, Int(arg));
				break;
			case Kind.StickerSources:
			case Kind.StickerEvents:
			case Kind.StickerTypes:
			case Kind.StickerAudience:
				card.Art = StickerNavArt(f, verb, arg) ?? Art.Folder;
				break;
			case Kind.Stickers:
			case Kind.StickerSearch:
				StickerCard(f, card, verb, Int(arg));
				break;
			default:
				Generic(f, card, item);
				break;
		}
		return card;
	}

	private const string MajorSource = "Major Stickers";

	private static string? StickerNavArt(Frame f, string verb, string arg)
	{
		string[] scope = f.Snap.Scope.Length > 0 ? f.Snap.Scope.Split('|') : Array.Empty<string>();
		string source;
		string? group = null, type = null, audience = null;
		switch (f.Snap.Kind)
		{
			case Kind.StickerSources when verb == "source":
				source = arg;
				break;
			case Kind.StickerEvents when verb == "group" && scope.Length >= 1:
				source = scope[0];
				group = arg;
				break;
			case Kind.StickerTypes when verb == "type" && scope.Length >= 2:
				source = scope[0];
				group = scope[1];
				type = arg;
				break;
			case Kind.StickerAudience when verb == "audience" && scope.Length >= 3:
				source = scope[0];
				group = scope[1];
				type = scope[2];
				audience = arg == "teams" ? "Teams" : "Players";
				break;
			default:
				return null;
		}
		int id = source == MajorSource ? MajorArt(f, source, group, type, audience) : 0;
		if (id <= 0)
		{
			id = f.Items.BestSticker(f.Catalog.StickerIds(source, group, type, audience));
		}
		return id > 0 ? Art.Sticker(id) : null;
	}

	private static int MajorArt(Frame f, string source, string? group, string? type, string? audience)
	{
		ICatalog catalog = f.Catalog;
		Func<int, bool> hasArt = id => f.Items.IsEmpty || f.Items.HasSticker(id);
		if (group == null)
		{
			return catalog.EventSticker(source, null, null, hasArt);
		}
		if (audience != null)
		{
			if (catalog.StickerIds(source, group, type, audience).Count == 0)
			{
				return 0;
			}
			int pick = catalog.EventSticker(source, group, audience == "Teams" ? "Normal" : "Holo", hasArt);
			if (pick > 0)
			{
				return pick;
			}
		}
		int own = type != null ? catalog.EventSticker(source, group, type, hasArt) : 0;
		return own > 0 ? own : catalog.EventSticker(source, group, null, hasArt);
	}

	private static void Generic(Frame f, Card card, ItemSnapshot item)
	{
		card.Sub = item.Value ?? (item.Toggle.HasValue ? f.T(item.Toggle.Value ? "on" : "off") : "");
	}

	private static void Category(Frame f, Card card, string category)
	{
		int def = category switch
		{
			"Knives" => 508,
			"Rifles" => 7,
			"Pistols" => 61,
			"SMGs" => 17,
			"Heavy" => 35,
			"Snipers" => 9,
			"Other" => 31,
			_ => 0
		};
		card.Base = def > 0 ? Art.Base(def) : null;
		card.Art = def > 0 ? null : Art.Folder;
		card.Plain = true;
		if (category == "Knives")
		{
			card.EqT = f.Loadout.T.Knife > 0;
			card.EqCt = f.Loadout.CT.Knife > 0;
			int knife = f.Team.Knife;
			card.Sub = knife > 0 ? f.Catalog.WeaponName(knife) ?? "" : f.T("default");
			return;
		}
		int t = 0;
		int ct = 0;
		foreach (int weapon in f.Catalog.CategoryWeapons(category))
		{
			if (PaintOn(f.Loadout.T, weapon) > 0)
			{
				t++;
			}
			if (PaintOn(f.Loadout.CT, weapon) > 0)
			{
				ct++;
			}
		}
		card.EqT = t > 0;
		card.EqCt = ct > 0;
		int mine = f.Ct ? ct : t;
		card.Sub = mine > 0 ? f.T("equipped.count", mine) : "";
	}

	private static void Weapon(Frame f, Card card, string verb, int def)
	{
		card.Base = Art.Base(def);
		bool knife = verb == "knife";
		int paint = PaintOn(f.Team, def);
		if (knife && f.Team.Knife != def)
		{
			paint = 0;
		}
		card.EqT = knife ? f.Loadout.T.Knife == def : PaintOn(f.Loadout.T, def) > 0;
		card.EqCt = knife ? f.Loadout.CT.Knife == def : PaintOn(f.Loadout.CT, def) > 0;
		card.Stock = paint <= 0;
		if (paint > 0)
		{
			card.Art = Art.Skin(def, paint);
			card.Rarity = f.Items.Skin(def, paint);
			card.Sub = f.Catalog.PaintName(def, paint) ?? "";
		}
		else
		{
			card.Sub = f.T("default");
		}
	}

	private static void Skin(Frame f, Card card, string verb, string arg)
	{
		int def = f.Snap.WeaponDef;
		card.Base = Art.Base(def);
		if (verb == "default")
		{
			card.Stock = true;
			card.EqT = PaintOn(f.Loadout.T, def) <= 0;
			card.EqCt = PaintOn(f.Loadout.CT, def) <= 0;
			return;
		}
		int paint = Int(arg);
		card.Name = StripId(card.Name, arg);
		card.Art = Art.Skin(def, paint);
		card.Rarity = f.Items.Skin(def, paint);
		card.Sub = RarityLabel(f, def, card.Rarity);
		card.EqT = PaintOn(f.Loadout.T, def) == paint;
		card.EqCt = PaintOn(f.Loadout.CT, def) == paint;
	}

	private static void KnifeCard(Frame f, Card card, string verb, int def)
	{
		if (verb == "default")
		{
			card.Base = Art.KnifeBase;
			card.Stock = true;
			card.EqT = f.Loadout.T.Knife <= 0;
			card.EqCt = f.Loadout.CT.Knife <= 0;
			return;
		}
		card.Base = Art.Base(def);
		card.EqT = f.Loadout.T.Knife == def;
		card.EqCt = f.Loadout.CT.Knife == def;
		int paint = f.Team.Knife == def ? PaintOn(f.Team, def) : 0;
		if (paint > 0)
		{
			card.Art = Art.Skin(def, paint);
			card.Rarity = f.Items.Skin(def, paint);
			card.Sub = f.Catalog.PaintName(def, paint) ?? "";
		}
	}

	private static void GloveCard(Frame f, Card card, string verb, int def)
	{
		if (verb == "default")
		{
			card.Art = Art.DefaultGloves(f.Ct);
			card.Stock = true;
			card.EqT = f.Loadout.T.GloveDef <= 0;
			card.EqCt = f.Loadout.CT.GloveDef <= 0;
			return;
		}
		TeamSnapshot team = f.Team;
		int paint = team.GloveDef == def && team.GlovePaint > 0 ? team.GlovePaint : f.Catalog.FirstPaint(def);
		card.Art = paint > 0 ? Art.Skin(def, paint) : null;
		card.Rarity = 6;
		card.EqT = f.Loadout.T.GloveDef == def;
		card.EqCt = f.Loadout.CT.GloveDef == def;
		if (team.GloveDef == def && team.GlovePaint > 0)
		{
			card.Sub = f.Catalog.PaintName(def, team.GlovePaint) ?? "";
		}
	}

	private static void GloveSkin(Frame f, Card card, string arg)
	{
		int def = f.Snap.GloveDef;
		int paint = Int(arg);
		card.Art = Art.Skin(def, paint);
		card.Rarity = f.Items.Skin(def, paint);
		card.Sub = RarityLabel(f, def, card.Rarity);
		card.EqT = f.Loadout.T.GloveDef == def && f.Loadout.T.GlovePaint == paint;
		card.EqCt = f.Loadout.CT.GloveDef == def && f.Loadout.CT.GlovePaint == paint;
	}

	private static void Faction(Frame f, Card card, string verb, string arg)
	{
		if (verb == "default")
		{
			card.Art = Art.DefaultAgent(f.Ct);
			card.Stock = true;
			card.EqT = f.Loadout.T.Agent == null;
			card.EqCt = f.Loadout.CT.Agent == null;
			return;
		}
		string? model = f.Catalog.FirstAgent(arg);
		card.Art = model != null ? Art.Agent(model) : Art.Folder;
	}

	private static void AgentCard(Frame f, Card card, string model)
	{
		card.Art = Art.Agent(model);
		card.Rarity = f.Items.Agent(model);
		card.Sub = card.Rarity > 0 ? ItemRarity(f, "agent", card.Rarity) : "";
		card.EqT = string.Equals(f.Loadout.T.Agent, model, StringComparison.Ordinal);
		card.EqCt = string.Equals(f.Loadout.CT.Agent, model, StringComparison.Ordinal);
	}

	private static void MusicCard(Frame f, Card card, string verb, int id)
	{
		if (verb == "default")
		{
			card.Art = Art.Music(0);
			card.Stock = true;
			card.EqT = card.EqCt = f.Loadout.Music <= 0;
			return;
		}
		card.Art = Art.Music(id);
		card.Rarity = Math.Max(3, f.Items.Music(id));
		card.EqT = card.EqCt = f.Loadout.Music == id;
		int comma = card.Name.IndexOf(", ", StringComparison.Ordinal);
		if (comma > 0)
		{
			card.Sub = card.Name[..comma].Trim();
			card.Name = card.Name[(comma + 2)..].Trim();
		}
	}

	private static void PinGroup(Frame f, Card card, string verb, string arg)
	{
		if (verb == "default")
		{
			card.Art = Art.Pin(0);
			card.Stock = true;
			card.EqT = card.EqCt = f.Loadout.Pin <= 0;
			return;
		}
		int pin = f.Catalog.FirstPin(arg);
		card.Art = pin > 0 ? Art.Pin(pin) : Art.Folder;
	}

	private static void PinCard(Frame f, Card card, int id)
	{
		card.Art = Art.Pin(id);
		card.Rarity = f.Items.Pin(id);
		card.Sub = card.Rarity > 0 ? ItemRarity(f, "item", card.Rarity) : "";
		card.EqT = card.EqCt = f.Loadout.Pin == id;
	}

	private static void StickerCard(Frame f, Card card, string verb, int id)
	{
		if (verb != "sticker")
		{
			card.Art = Art.Folder;
			return;
		}
		card.Art = Art.Sticker(id);
		card.Rarity = f.Items.Sticker(id);
		card.Sub = card.Rarity > 0 ? ItemRarity(f, "item", card.Rarity) : "";
		int slot = f.Snap.StickerSlot;
		card.EqT = StickerOn(f.Loadout.T.Weapon(f.Snap.WeaponDef), slot, id);
		card.EqCt = StickerOn(f.Loadout.CT.Weapon(f.Snap.WeaponDef), slot, id);
	}

	private static bool StickerOn(WeaponSnapshot? weapon, int slot, int id)
	{
		if (weapon == null)
		{
			return false;
		}
		return slot >= 0 && slot < weapon.Stickers.Length ? weapon.Stickers[slot] == id : weapon.Stickers.Contains(id);
	}


	private static WeaponSnapshot? CurrentEntry(Frame f)
	{
		if (IsGloveKind(f.Snap.Kind))
		{
			return f.Team.Gloves;
		}
		return f.Snap.WeaponDef > 0 ? f.Team.Weapon(f.Snap.WeaponDef) : null;
	}

	private static bool IsGloveKind(string kind) => kind is Kind.GloveCustomize or Kind.GloveWear or Kind.GloveSeed;

	public static Row MakeRow(Frame f, int index)
	{
		ItemSnapshot item = f.Snap.Item(index);
		Split(item.Tag, out string verb, out string arg);
		Row row = new() { Name = item.Text.Trim(), Value = item.Value ?? "", Disabled = f.Disabled(index) };
		WeaponSnapshot? entry = CurrentEntry(f);
		switch (verb)
		{
			case "stattrak":
				bool on = entry != null ? entry.StatTrak >= 0 : item.Toggle == true;
				row.On = on;
				row.Value = f.T(on ? "on" : "off");
				break;
			case "wear" when arg.Length == 0:
				if (entry != null)
				{
					row.Value = WearText(f, entry.Wear);
				}
				break;
			case "wear":
				row.Eq = entry != null && IsNearestPreset(f, entry.Wear, index);
				break;
			case "seed":
				if (entry != null)
				{
					row.Value = entry.Seed.ToString(CultureInfo.InvariantCulture);
				}
				break;
			case "nametag":
				row.Value = entry?.NameTag ?? "";
				break;
			case "nametag-change":
				row.Value = entry?.NameTag ?? "";
				break;
			case "stickers":
				if (entry != null)
				{
					row.Value = f.T("slots.used", entry.StickerCount);
				}
				break;
			case "search":
				row.Name = f.T("row.search");
				row.Center = row.Value.Length == 0;
				break;
			case "remove":
				row.Name = f.T("row.remove");
				row.Center = true;
				break;
			case "remove-all":
				row.Name = f.T("row.remove_all");
				row.Center = true;
				break;
			case "slot":
				int slot = Int(arg);
				if (f.Snap.Kind == Kind.StickerRemove)
				{
					row.Name = f.T("row.remove_slot", slot + 1);
				}
				if (entry != null && slot >= 0 && slot < entry.Stickers.Length && entry.Stickers[slot] > 0)
				{
					row.Art = Art.Sticker(entry.Stickers[slot]);
				}
				break;
			default:
				if (item.Toggle.HasValue)
				{
					row.On = item.Toggle.Value;
					row.Value = f.T(item.Toggle.Value ? "on" : "off");
				}
				break;
		}
		return row;
	}

	private static string WearName(Frame f, float wear)
	{
		string key = wear < 0.07f ? "fn" : wear < 0.15f ? "mw" : wear < 0.38f ? "ft" : wear < 0.45f ? "ww" : "bs";
		return f.T("wear." + key);
	}

	private static string WearText(Frame f, float wear)
	{
		return wear.ToString("0.######", CultureInfo.InvariantCulture) + "  ·  " + WearName(f, wear);
	}

	private static bool IsNearestPreset(Frame f, float wear, int index)
	{
		float best = float.MaxValue;
		int bestIndex = -1;
		for (int i = 0; i < f.Snap.Count; i++)
		{
			ItemSnapshot item = f.Snap.Item(i);
			Split(item.Tag, out string verb, out string arg);
			if (verb != "wear" || arg.Length == 0 || !float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
			{
				continue;
			}
			float distance = Math.Abs(value - wear);
			if (distance < best)
			{
				best = distance;
				bestIndex = i;
			}
		}
		return bestIndex == index;
	}

	public static Preview MakePreview(Frame f)
	{
		Preview preview = new() { Title = f.Snap.Title };
		bool glove = IsGloveKind(f.Snap.Kind);
		int def = glove ? f.Team.GloveDef : f.Snap.WeaponDef;
		if (def <= 0)
		{
			return preview;
		}
		WeaponSnapshot? entry = CurrentEntry(f);
		int paint = glove ? f.Team.GlovePaint : entry?.Paint ?? 0;
		preview.Sub = f.Catalog.WeaponName(def) ?? "";
		preview.Base = glove ? null : Art.Base(def);
		if (paint > 0)
		{
			preview.Art = Art.Skin(def, paint);
			preview.Rarity = f.Items.Skin(def, paint);
			preview.Title = f.Catalog.PaintName(def, paint) ?? f.Snap.Title;
		}
		if (entry != null)
		{
			preview.Lines[0] = f.T("line.wear", WearText(f, entry.Wear));
			preview.Lines[1] = f.T("line.seed", entry.Seed);
			if (!glove)
			{
				preview.Lines[2] = f.T("line.stattrak", f.T(entry.StatTrak >= 0 ? "on" : "off"));
				preview.Lines[3] = string.IsNullOrEmpty(entry.NameTag) ? "" : f.T("line.nametag", entry.NameTag);
				Array.Copy(entry.Stickers, preview.Stickers, preview.Stickers.Length);
			}
		}
		preview.ShowStickers = !glove && !IsKnife(def) && StickerPreviewKinds.Contains(f.Snap.Kind);
		preview.Live = preview.ShowStickers
			&& (f.Snap.Kind == Kind.StickerSlots || (f.Snap.Kind == Kind.Customize && IndexOfTag(f.Snap, "stickers") >= 0));
		preview.All = preview.ShowStickers && f.Snap.Kind == Kind.StickerSlots && IndexOfTag(f.Snap, "slot-all") >= 0;
		return preview;
	}


	public static RailInfo MakeRail(Frame f, string key)
	{
		TeamSnapshot team = f.Team;
		RailInfo info = new();
		switch (key)
		{
			case "weapons":
				info.Sub = f.T("sub.weapons");
				break;
			case "stickers":
				info.Sub = f.T("sub.stickers");
				break;
			case "knife":
			{
				int def = team.Knife;
				if (def <= 0)
				{
					info.Sub = f.T("default");
					break;
				}
				string name = f.Catalog.WeaponName(def) ?? "";
				int paint = PaintOn(team, def);
				string? paintName = paint > 0 ? f.Catalog.PaintName(def, paint) : null;
				info.Sub = paintName != null ? name + "  |  " + paintName : name;
				info.Art = paint > 0 ? Art.Skin(def, paint) : Art.Base(def);
				break;
			}
			case "gloves":
			{
				if (team.GloveDef <= 0)
				{
					info.Sub = f.T("default");
					info.Art = Art.DefaultGloves(f.Ct);
					break;
				}
				string name = f.Catalog.WeaponName(team.GloveDef) ?? "";
				string? paintName = team.GlovePaint > 0 ? f.Catalog.PaintName(team.GloveDef, team.GlovePaint) : null;
				info.Sub = paintName != null ? name + "  |  " + paintName : name;
				info.Art = team.GlovePaint > 0 ? Art.Skin(team.GloveDef, team.GlovePaint) : null;
				break;
			}
			case "agents":
			{
				if (team.Agent == null)
				{
					info.Sub = f.T("default");
					info.Art = Art.DefaultAgent(f.Ct);
					break;
				}
				info.Sub = f.Catalog.AgentName(team.Agent) ?? Art.AgentKey(team.Agent);
				info.Art = Art.Agent(team.Agent);
				break;
			}
			case "music":
			{
				int id = f.Loadout.Music;
				info.Sub = id <= 0 ? f.T("default") : f.Catalog.MusicName(id) ?? "#" + id;
				info.Art = Art.Music(id);
				break;
			}
			case "pins":
			{
				int id = f.Loadout.Pin;
				info.Sub = id <= 0 ? f.T("none") : f.Catalog.PinName(id) ?? "#" + id;
				info.Art = Art.Pin(id);
				break;
			}
		}
		return info;
	}
}
