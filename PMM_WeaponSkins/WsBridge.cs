using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Extensions.Logging;

namespace PMM_WeaponSkins;

internal readonly record struct ItemSnapshot(string Text, string? Value, bool? Toggle, string? Tag);

internal sealed class MenuSnapshot
{
	public string Kind = "";
	public string Title = "";
	public int Depth = 1;
	public int Count;
	public int WeaponDef;
	public int GloveDef;
	public int StickerSlot = -1;
	public string? Prompt;

	public string Scope = "";

	public Func<int, ItemSnapshot> Item = _ => default;

	public Func<int, string?> Tag = _ => null;

	public MenuInfo? Info;

	public bool Typed => Kind.Length > 0;
}

internal sealed class WeaponSnapshot
{
	public int Paint;
	public float Wear;
	public int Seed;
	public string? NameTag;
	public int StatTrak = -1;
	public readonly int[] Stickers = new int[4];

	public int StickerCount => Stickers.Count(id => id > 0);
}

internal sealed class TeamSnapshot
{
	public int Knife;
	public int GloveDef;
	public int GlovePaint;
	public string? Agent;
	public readonly Dictionary<int, WeaponSnapshot> Weapons = new();
	public WeaponSnapshot? Gloves;

	public WeaponSnapshot? Weapon(int def) => Weapons.TryGetValue(def, out WeaponSnapshot? weapon) ? weapon : null;
}

internal sealed class LoadoutSnapshot
{
	public readonly TeamSnapshot T = new();
	public readonly TeamSnapshot CT = new();
	public int Music;
	public int Pin;

	public TeamSnapshot Team(bool ct) => ct ? CT : T;
}

internal sealed class WsBridge : ICatalog
{
	private readonly ILogger _logger;
	private PropertyInfo? _instance;
	private int _warned;

	private Dictionary<string, string>? _agents;
	private Dictionary<int, string>? _music;
	private Dictionary<int, string>? _pins;
	private long _namesAt;

	public WsBridge(ILogger logger)
	{
		_logger = logger;
	}

	private object? Plugin()
	{
		for (int attempt = 0; attempt < 2; attempt++)
		{
			if (_instance == null)
			{
				foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
				{
					PropertyInfo? property = null;
					try
					{
						property = assembly.GetType("WeaponSkins.WeaponSkins", false)?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
					}
					catch (Exception)
					{
					}
					if (property != null && SafeValue(property) != null)
					{
						_instance = property;
						break;
					}
				}
			}
			object? value = _instance != null ? SafeValue(_instance) : null;
			if (value != null)
			{
				return value;
			}
			_instance = null;
		}
		return null;
	}

	private static object? SafeValue(PropertyInfo property)
	{
		try
		{
			return property.GetValue(null);
		}
		catch (Exception)
		{
			return null;
		}
	}

	public bool Available => Plugin() != null;

	private HashSet<int>? _gloveDefs;
	private int _gloveCount = -1;

	public bool IsGlove(int def)
	{
		object? plugin = Plugin();
		if (plugin != null)
		{
			try
			{
				dynamic gloves = ((dynamic)plugin).Catalog.Gloves;
				int count = (int)gloves.Count;
				if (_gloveDefs == null || count != _gloveCount)
				{
					HashSet<int> set = new();
					foreach (dynamic glove in (IEnumerable)gloves)
					{
						set.Add((int)glove.DefIndex);
					}
					_gloveDefs = set;
					_gloveCount = count;
				}
				if (_gloveDefs.Count > 0)
				{
					return _gloveDefs.Contains(def);
				}
			}
			catch (Exception)
			{
			}
		}
		return CardFactory.IsGloveFallback(def);
	}

	public string? Version()
	{
		object? plugin = Plugin();
		if (plugin == null)
		{
			return null;
		}
		try
		{
			return (string?)((dynamic)plugin).ModuleVersion;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private void Warn(string what, Exception ex)
	{
		if (Interlocked.Exchange(ref _warned, 1) == 0)
		{
			_logger.LogWarning("WeaponSkins could not be read ({What}): {Message}. PMM_WeaponSkins needs WeaponSkins 1.1.8 or newer.", what, ex.GetBaseException().Message);
		}
	}

	private static bool TryGet<T>(Func<T> read, out T value)
	{
		try
		{
			value = read();
			return true;
		}
		catch (RuntimeBinderException)
		{
			value = default!;
			return false;
		}
	}


	public MenuSnapshot? Read(CCSPlayerController player, IMenu? painted)
	{
		object? plugin = Plugin();
		if (plugin == null)
		{
			return null;
		}
		try
		{
			dynamic ws = plugin;
			object? sessionObject = ws.Menu.SessionOf(player);
			if (sessionObject is null)
			{
				return null;
			}
			dynamic session = sessionObject;
			if (!(bool)session.Native)
			{
				return null;
			}
			object? currentObject = session.Current;
			if (currentObject is null)
			{
				return null;
			}
			dynamic current = currentObject;
			string title = (string?)current.Title ?? "";
			IReadOnlyList<object> items = (IReadOnlyList<object>)current.Items;
			int count = items.Count;
			if (painted != null && !LooksLike(painted, title, items))
			{
				return null;
			}

			MenuSnapshot snapshot = new()
			{
				Title = title,
				Count = count,
				Depth = Math.Max(1, (int)session.Stack.Count),
				Prompt = session.Input == null ? null : (string?)session.Input.Prompt
			};
			MenuInfo info = InfoOf(currentObject, session, items);
			snapshot.Kind = info.Kind;
			snapshot.Scope = info.Scope;
			snapshot.Info = info;
			dynamic ctx = session.Ctx;
			snapshot.WeaponDef = (int)ctx.WeaponDef;
			snapshot.GloveDef = (int)ctx.GloveDef;
			snapshot.StickerSlot = (int)ctx.StickerSlot;
			snapshot.Item = index =>
			{
				try
				{
					dynamic item = items[index];
					return new ItemSnapshot((string?)item.Text ?? "", (string?)item.Value, (bool?)item.Toggle, info.Tag(index));
				}
				catch (Exception)
				{
					return default;
				}
			};
			snapshot.Tag = info.Tag;
			return snapshot;
		}
		catch (Exception ex)
		{
			Warn("menu", ex);
			return null;
		}
	}

	private readonly ConditionalWeakTable<object, MenuInfo> _infos = new();
	private readonly HashSet<string> _unknownBuilders = new(StringComparer.Ordinal);

	private MenuInfo InfoOf(object menu, dynamic session, IReadOnlyList<object> items)
	{
		if (_infos.TryGetValue(menu, out MenuInfo? known))
		{
			return known;
		}
		Delegate? factory = null;
		try
		{
			IList stack = (IList)session.Stack;
			object? top = stack.Count > 0 ? stack[stack.Count - 1] : null;
			factory = top?.GetType().GetField("Item1")?.GetValue(top) as Delegate;
		}
		catch (Exception)
		{
		}
		MenuInfo info = WsSemantics.Describe(items, factory, builder =>
		{
			if (_unknownBuilders.Add(builder))
			{
				_logger.LogWarning("The WeaponSkins menu built by '{Builder}' is not known to PMM_WeaponSkins and is drawn as plain cards. WeaponSkins is newer than this PMM_WeaponSkins.", builder);
			}
		});
		_infos.AddOrUpdate(menu, info);
		return info;
	}

	private static bool LooksLike(IMenu painted, string title, IReadOnlyList<object> items)
	{
		if (!string.Equals(painted.Title?.Trim(), title.Trim(), StringComparison.Ordinal) || painted.MenuOptions.Count != items.Count)
		{
			return false;
		}
		for (int i = 0; i < Math.Min(3, items.Count); i++)
		{
			string? text = (string?)((dynamic)items[i]).Text;
			string shown = painted.MenuOptions[i].Text ?? "";
			if (!string.IsNullOrEmpty(text) && !shown.Contains(text, StringComparison.Ordinal))
			{
				return false;
			}
		}
		return true;
	}

	public MenuSnapshot? Peek(CCSPlayerController player) => Read(player, null);

	public void Back(CCSPlayerController player)
	{
		Run("back", ws =>
		{
			dynamic? session = ws.Menu.SessionOf(player);
			if (session != null)
			{
				ws.Menu.Back(player, session);
			}
		});
	}

	public void Close(CCSPlayerController player)
	{
		Run("close", ws => ws.Menu.Close(player));
	}

	public void CancelPrompt(CCSPlayerController player)
	{
		Run("prompt", ws => ws.Menu.CaptureChat(player, "-"));
	}

	private void Run(string what, Action<dynamic> action)
	{
		object? plugin = Plugin();
		if (plugin == null)
		{
			return;
		}
		try
		{
			action(plugin);
		}
		catch (Exception ex)
		{
			Warn(what, ex);
		}
	}

	public int? MenuStyle()
	{
		object? plugin = Plugin();
		if (plugin == null)
		{
			return null;
		}
		try
		{
			return (int)((dynamic)plugin).Config.Menu.MenuStyle;
		}
		catch (Exception ex)
		{
			Warn("config", ex);
			return null;
		}
	}

	public string Command(string key)
	{
		string fallback = key switch
		{
			"skins" => "ws",
			"pins" => "pins",
			_ => key
		};
		object? plugin = Plugin();
		if (plugin == null)
		{
			return fallback;
		}
		try
		{
			dynamic commands = ((dynamic)plugin).Config.Commands;
			IList? list = key switch
			{
				"skins" => (IList?)commands.Skins,
				"knife" => (IList?)commands.Knife,
				"gloves" => (IList?)commands.Gloves,
				"agents" => (IList?)commands.Agents,
				"music" => (IList?)commands.Music,
				"pins" => (IList?)commands.Pins,
				"stickers" => (IList?)commands.Stickers,
				_ => null
			};
			if (list != null)
			{
				foreach (object? entry in list)
				{
					string name = entry?.ToString()?.Trim().TrimStart('!', '/') ?? "";
					if (name.StartsWith("css_", StringComparison.OrdinalIgnoreCase))
					{
						name = name[4..];
					}
					if (name.Length > 0)
					{
						return name;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Warn("commands", ex);
		}
		return fallback;
	}


	public LoadoutSnapshot? Loadout(CCSPlayerController player)
	{
		object? plugin = Plugin();
		if (plugin == null)
		{
			return null;
		}
		try
		{
			dynamic ws = plugin;
			dynamic? loadout = ws.Cache.Get(player);
			if (loadout == null)
			{
				return null;
			}
			LoadoutSnapshot snapshot = new()
			{
				Music = (int)loadout.MusicKit,
				Pin = (int)loadout.Pin
			};
			ReadTeam(loadout.T, snapshot.T);
			ReadTeam(loadout.CT, snapshot.CT);
			return snapshot;
		}
		catch (Exception ex)
		{
			Warn("loadout", ex);
			return null;
		}
	}

	private static void ReadTeam(dynamic team, TeamSnapshot target)
	{
		target.Knife = (int)team.Knife;
		target.GloveDef = (int)team.GloveDef;
		target.GlovePaint = (int)team.GlovePaint;
		target.Agent = (string?)team.AgentModel;
		foreach (DictionaryEntry entry in (IDictionary)team.Weapons)
		{
			target.Weapons[(int)entry.Key] = ReadWeapon(entry.Value!);
		}
		if (target.GloveDef > 0 && target.GlovePaint > 0)
		{
			IDictionary skins = (IDictionary)team.Skins;
			if (skins.Contains(target.GloveDef) && skins[target.GloveDef] is IDictionary byPaint && byPaint.Contains(target.GlovePaint))
			{
				target.Gloves = ReadWeapon(byPaint[target.GlovePaint]!);
			}
		}
	}

	private static WeaponSnapshot ReadWeapon(dynamic entry)
	{
		WeaponSnapshot weapon = new()
		{
			Paint = (int)entry.Paint,
			Wear = (float)entry.Wear,
			Seed = (int)entry.Seed,
			NameTag = (string?)entry.NameTag,
			StatTrak = (int)entry.StatTrak
		};
		foreach (dynamic sticker in (IEnumerable)entry.Stickers)
		{
			int slot = (int)sticker.Slot;
			if (slot >= 0 && slot < weapon.Stickers.Length)
			{
				weapon.Stickers[slot] = (int)sticker.Id;
			}
		}
		return weapon;
	}


	public string? WeaponName(int def)
	{
		object? plugin = Plugin();
		if (plugin == null || def <= 0)
		{
			return null;
		}
		try
		{
			string? name = (string?)((dynamic)plugin).Catalog.WeaponName(def);
			return string.IsNullOrEmpty(name) || name[0] == '#' ? null : name;
		}
		catch (Exception ex)
		{
			Warn("catalogue", ex);
			return null;
		}
	}

	public string? PaintName(int def, int paint)
	{
		object? plugin = Plugin();
		if (plugin == null || def <= 0 || paint <= 0)
		{
			return null;
		}
		try
		{
			dynamic? found = ((dynamic)plugin).Catalog.FindPaint(def, paint);
			return found == null ? null : (string?)found.Name;
		}
		catch (Exception ex)
		{
			Warn("catalogue", ex);
			return null;
		}
	}

	public int FirstPaint(int def)
	{
		object? plugin = Plugin();
		if (plugin == null || def <= 0)
		{
			return 0;
		}
		try
		{
			dynamic paints = ((dynamic)plugin).Catalog.Paints;
			if (paints.ContainsKey(def))
			{
				dynamic list = paints[def];
				if ((int)list.Count > 0)
				{
					return (int)list[0].Paint;
				}
			}
		}
		catch (Exception ex)
		{
			Warn("catalogue", ex);
		}
		return 0;
	}

	public List<int> CategoryWeapons(string category)
	{
		List<int> result = new();
		object? plugin = Plugin();
		if (plugin == null)
		{
			return result;
		}
		try
		{
			dynamic weapons = ((dynamic)plugin).Catalog.WeaponsByCategory;
			if (weapons.ContainsKey(category))
			{
				foreach (dynamic weapon in weapons[category])
				{
					result.Add((int)weapon.DefIndex);
				}
			}
		}
		catch (Exception ex)
		{
			Warn("catalogue", ex);
		}
		return result;
	}

	public string? AgentName(string model)
	{
		RefreshNames();
		return _agents != null && _agents.TryGetValue(Art.AgentKey(model), out string? name) ? name : null;
	}

	public string? MusicName(int id)
	{
		RefreshNames();
		return _music != null && _music.TryGetValue(id, out string? name) ? name : null;
	}

	public string? PinName(int id)
	{
		RefreshNames();
		return _pins != null && _pins.TryGetValue(id, out string? name) ? name : null;
	}

	public string? FirstAgent(string faction)
	{
		object? plugin = Plugin();
		if (plugin == null)
		{
			return null;
		}
		try
		{
			dynamic catalog = ((dynamic)plugin).Catalog;
			foreach (dynamic groups in new dynamic[] { catalog.AgentsByFactionCT, catalog.AgentsByFactionT })
			{
				if (groups.ContainsKey(faction))
				{
					dynamic list = groups[faction];
					if ((int)list.Count > 0)
					{
						return (string?)list[0].Model;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Warn("catalogue", ex);
		}
		return null;
	}

	public int FirstPin(string group)
	{
		object? plugin = Plugin();
		if (plugin == null)
		{
			return 0;
		}
		try
		{
			dynamic groups = ((dynamic)plugin).Catalog.PinsByGroup;
			if (groups.ContainsKey(group))
			{
				dynamic list = groups[group];
				if ((int)list.Count > 0)
				{
					return (int)list[0].Id;
				}
			}
		}
		catch (Exception ex)
		{
			Warn("catalogue", ex);
		}
		return 0;
	}


	private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private readonly Dictionary<string, int[]> _stickerIds = new(StringComparer.Ordinal);
	private object? _stickerPlugin;
	private object? _stickerBrowser;
	private bool _stickerApiMissing;

	public IReadOnlyList<int> StickerIds(string source, string? group, string? type, string? audience)
	{
		object? plugin = Plugin();
		if (plugin == null)
		{
			return Array.Empty<int>();
		}
		if (!ReferenceEquals(plugin, _stickerPlugin))
		{
			_stickerPlugin = plugin;
			_stickerBrowser = null;
			_stickerIds.Clear();
			_stickerApiMissing = false;
		}
		if (_stickerApiMissing)
		{
			return Array.Empty<int>();
		}
		try
		{
			object menus = ((dynamic)plugin).Menus;
			FieldInfo? field = menus.GetType().GetField("stickerBrowser", AnyInstance);
			if (field == null)
			{
				_stickerApiMissing = true;
				return Array.Empty<int>();
			}
			object? browser = field.GetValue(menus);
			if (browser == null)
			{
				return Array.Empty<int>();
			}
			if (!ReferenceEquals(browser, _stickerBrowser))
			{
				_stickerBrowser = browser;
				_stickerIds.Clear();
			}
			string key = source + "\u0001" + group + "\u0001" + type + "\u0001" + audience;
			if (_stickerIds.TryGetValue(key, out int[]? known))
			{
				return known;
			}
			List<int> ids = new();
			CollectStickers(browser, source, group, type, audience ?? "", ids);
			int[] result = ids.ToArray();
			if (result.Length > 0)
			{
				_stickerIds[key] = result;
			}
			return result;
		}
		catch (Exception ex)
		{
			_stickerApiMissing = true;
			Warn("stickers", ex);
			return Array.Empty<int>();
		}
	}

	private HashSet<int>? _eventIds;
	private object? _eventList;
	private int _eventCount = -1;

	private HashSet<int> EventIds()
	{
		object? plugin = Plugin();
		if (plugin == null)
		{
			return new HashSet<int>();
		}
		try
		{
			dynamic catalog = ((dynamic)plugin).Catalog;
			object? list = catalog.Stickers;
			if (list == null)
			{
				return new HashSet<int>();
			}
			int count = (int)((dynamic)list).Count;
			if (_eventIds != null && ReferenceEquals(list, _eventList) && count == _eventCount)
			{
				return _eventIds;
			}
			HashSet<int> set = new();
			foreach (dynamic sticker in (IEnumerable)list)
			{
				if (string.Equals((string?)sticker.Kind, "Event", StringComparison.OrdinalIgnoreCase))
				{
					set.Add((int)sticker.Id);
				}
			}
			_eventIds = set;
			_eventList = list;
			_eventCount = count;
			return set;
		}
		catch (Exception ex)
		{
			Warn("catalogue", ex);
			return new HashSet<int>();
		}
	}

	public int EventSticker(string source, string? group, string? type, Func<int, bool>? hasArt = null)
	{
		HashSet<int> events = EventIds();
		if (events.Count == 0)
		{
			return 0;
		}
		if (group == null)
		{
			return PickEvent(StickerIds(source, null, "Normal", null), events, true, hasArt);
		}
		if (type != null)
		{
			return PickEvent(StickerIds(source, group, type, null), events, false, hasArt);
		}
		int plain = PickEvent(StickerIds(source, group, "Normal", null), events, false, hasArt);
		return plain > 0 ? plain : PickEvent(StickerIds(source, group, null, null), events, false, hasArt);
	}

	private static int PickEvent(IReadOnlyList<int> ids, HashSet<int> events, bool newest, Func<int, bool>? hasArt)
	{
		int best = 0;
		foreach (int id in ids)
		{
			if (events.Contains(id) && (hasArt == null || hasArt(id)) && (best == 0 || (newest ? id > best : id < best)))
			{
				best = id;
			}
		}
		return best;
	}

	private static IDictionary BrowserTable(object browser, string name)
	{
		return (IDictionary)(browser.GetType().GetField(name, AnyInstance)?.GetValue(browser)
			?? throw new MissingFieldException(browser.GetType().Name, name));
	}

	private static void CollectStickers(object browser, string source, string? group, string? type, string audience, List<int> ids)
	{
		if (group == null)
		{
			IList? groups = BrowserTable(browser, "Groups")[source] as IList;
			if (groups == null || groups.Count == 0)
			{
				CollectStickers(browser, source, "", type, audience, ids);
				return;
			}
			foreach (object? name in groups)
			{
				CollectStickers(browser, source, (string?)name ?? "", type, audience, ids);
			}
			return;
		}
		if (type == null)
		{
			if (BrowserTable(browser, "Types")[source + "|" + group] is IList types)
			{
				foreach (object? name in types)
				{
					CollectStickers(browser, source, group, (string?)name ?? "", audience, ids);
				}
			}
			return;
		}
		if (BrowserTable(browser, "Lists")[source + "|" + group + "|" + type + "|" + audience] is not IList list)
		{
			return;
		}
		foreach (object? item in list)
		{
			int id = item == null ? 0 : WsSemantics.StickerId(item);
			if (id > 0)
			{
				ids.Add(id);
			}
		}
	}


	public int CatalogKey()
	{
		object? plugin = Plugin();
		if (plugin == null)
		{
			return -1;
		}
		try
		{
			dynamic catalog = ((dynamic)plugin).Catalog;
			if (!(bool)catalog.Loaded)
			{
				return -1;
			}
		int skins = 0;
		foreach (DictionaryEntry entry in (IDictionary)catalog.Paints)
		{
			skins += ((IList)entry.Value!).Count;
		}
		return unchecked(skins * 31 + (int)catalog.Stickers.Count * 17 + (int)catalog.AgentsT.Count * 7 + (int)catalog.AgentsCT.Count * 5
			+ (int)catalog.MusicKits.Count * 3 + (int)catalog.Pins.Count);
		}
		catch (Exception ex)
		{
			Warn("catalogue", ex);
			return -1;
		}
	}

	public CatalogContents? CatalogContents()
	{
		object? plugin = Plugin();
		if (plugin == null)
		{
			return null;
		}
		try
		{
			dynamic catalog = ((dynamic)plugin).Catalog;
			CatalogContents contents = new();
			foreach (DictionaryEntry entry in (IDictionary)catalog.Paints)
			{
				foreach (dynamic paint in (IEnumerable)entry.Value!)
				{
					contents.Skins.Add(((int)entry.Key, (int)paint.Paint));
				}
			}
			foreach (dynamic sticker in (IEnumerable)catalog.Stickers)
			{
				contents.Stickers.Add((int)sticker.Id);
			}
			foreach (dynamic list in new dynamic[] { catalog.AgentsT, catalog.AgentsCT })
			{
				foreach (dynamic agent in (IEnumerable)list)
				{
					contents.Agents.Add((string)agent.Model);
				}
			}
			foreach (dynamic kit in (IEnumerable)catalog.MusicKits)
			{
				contents.Music.Add((int)kit.Id);
			}
			foreach (dynamic pin in (IEnumerable)catalog.Pins)
			{
				contents.Pins.Add((int)pin.Id);
			}
			return contents;
		}
		catch (Exception ex)
		{
			Warn("catalogue", ex);
			return null;
		}
	}

	private void RefreshNames()
	{
		long now = Environment.TickCount64;
		if (_agents != null && now - _namesAt < 60_000)
		{
			return;
		}
		_namesAt = now;
		object? plugin = Plugin();
		if (plugin == null)
		{
			return;
		}
		try
		{
			dynamic catalog = ((dynamic)plugin).Catalog;
			Dictionary<string, string> agents = new(StringComparer.Ordinal);
			foreach (dynamic list in new dynamic[] { catalog.AgentsT, catalog.AgentsCT })
			{
				foreach (dynamic agent in list)
				{
					agents[Art.AgentKey((string)agent.Model)] = (string)agent.Name;
				}
			}
			Dictionary<int, string> music = new();
			foreach (dynamic kit in catalog.MusicKits)
			{
				music[(int)kit.Id] = (string)kit.Name;
			}
			Dictionary<int, string> pins = new();
			foreach (dynamic pin in catalog.Pins)
			{
				pins[(int)pin.Id] = (string)pin.Name;
			}
			_agents = agents;
			_music = music;
			_pins = pins;
		}
		catch (Exception ex)
		{
			Warn("catalogue", ex);
		}
	}
}
