using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using MenuManager;
using Microsoft.Extensions.Logging;
using static CounterStrikeSharp.API.Core.Listeners;

namespace PMM_WeaponSkins;

public class PluginConfig : BasePluginConfig
{
	[JsonPropertyName("Language")] public string Language { get; set; } = "en";
	[JsonPropertyName("OpenDelay")] public float OpenDelay { get; set; } = 0.2f;
	[JsonPropertyName("InputDelay")] public float InputDelay { get; set; } = 0.2f;
	[JsonPropertyName("CardsPerPage")] public int CardsPerPage { get; set; } = 40;
	[JsonPropertyName("PageResetDelay")] public float PageResetDelay { get; set; } = 0.1f;
}

public partial class Plugin : BasePlugin, IPluginConfig<PluginConfig>, IPmmMenuPainter
{
	private const string LayoutPath = "panorama/layout/custom_game/ws.xml";
	private const int MaxSlots = 64;
	private const long ClickCooldownMs = 120;
	private const long PendingMs = 2000;

	private static readonly string[] TestedWeaponSkins = { "1.1.8", "1.1.9" };

	private sealed class Slot
	{
		public bool Open;
		public bool Shown;
		public int Token;
		public int PaintSeq;
		public IMenu? Menu;
		public int Page;
		public int PagerStart;
		public string? PageKey;
		public string RootKind = "";
		public bool Prompting;
		public bool CaptureArmed;
		public bool Captured;
		public bool RefreshQueued;

		public string? GridKey;
		public int GridCards;
		public int FlipToken;
		public long NextClick;

		public int[]? Map;
		public IMenu? MapMenu;

		public int PendingSquare = -1;
		public long PendingUntil;
		public readonly List<string> Trail = new();
		public readonly Dictionary<string, int> Pages = new(StringComparer.Ordinal);

		public readonly Dictionary<string, bool> SentClass = new(StringComparer.Ordinal);
		public readonly Dictionary<string, string> SentVar = new(StringComparer.Ordinal);
		public readonly string?[] CardArt = new string?[Layout.CardCount];
		public readonly string?[] CardBase = new string?[Layout.CardCount];
		public readonly string?[] CardRarity = new string?[Layout.CardCount];
		public readonly string?[] RowArt = new string?[Layout.RowCount];
		public readonly string?[] RailArt = new string?[Layout.Rail.Length];
		public readonly string?[] PreviewSticker = new string?[Layout.StickerSlots];
		public string? PreviewArt;
		public string? PreviewBase;
		public string? PreviewRarity;

		public int Index(int position)
		{
			if (Map == null)
			{
				return position;
			}
			return ReferenceEquals(MapMenu, Menu) && position >= 0 && position < Map.Length ? Map[position] : -1;
		}

		public void ForgetSent()
		{
			FlipToken++;
			GridKey = null;
			GridCards = 0;
			SentClass.Clear();
			SentVar.Clear();
			Array.Clear(CardArt);
			Array.Clear(CardBase);
			Array.Clear(CardRarity);
			Array.Clear(RowArt);
			Array.Clear(RailArt);
			Array.Clear(PreviewSticker);
			PreviewArt = PreviewBase = PreviewRarity = null;
		}

		public void ForgetNavigation()
		{
			Page = 0;
			PagerStart = 0;
			PageKey = null;
			RootKind = "";
			Trail.Clear();
			Pages.Clear();
		}
	}

	private readonly PluginCapability<IPmmPaintRegistry?> _paint = new("pmm:paint");
	private readonly PluginCapability<IMenuApi?> _menus = new("menu:nfcore");
	private readonly ItemIndex _items = new();
	private readonly Slot[] _slots = new Slot[MaxSlots];

	private WsBridge _bridge = null!;
	private CCSCustomHudLayout? _layout;
	private IPmmPaintRegistry? _registry;
	private bool _painterAdded;
	private bool _layoutWarned;
	private int _artKey = -2;

	public PluginConfig Config { get; set; } = new();

	public override string ModuleName => "PMM_WeaponSkins";

	public override string ModuleVersion => typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

	public override string ModuleAuthor => "✪ Stαr";

	public override string ModuleDescription => "Panorama UI for WeaponSkins. Needs Menu_Style 3 in the WeaponSkins config.";

	private string Language => Lang.Normalize(Config.Language);

	private int PageSize => Config.CardsPerPage;

	public void OnConfigParsed(PluginConfig config)
	{
		config.Language = Lang.Normalize(config.Language);
		config.OpenDelay = Math.Max(0f, config.OpenDelay);
		config.InputDelay = Math.Max(0f, config.InputDelay);
		config.CardsPerPage = config.CardsPerPage <= 0
			? Layout.CardCount
			: Math.Clamp(config.CardsPerPage / Layout.CardsPerRow * Layout.CardsPerRow, Layout.CardsPerRow, Layout.CardCount);
		config.PageResetDelay = Math.Clamp(config.PageResetDelay, 0f, 1f);
		Config = config;
	}

	public override void Load(bool hotReload)
	{
		_bridge = new WsBridge(Logger);
		for (int i = 0; i < MaxSlots; i++)
		{
			_slots[i] = new Slot();
		}
		_items.Load(ModuleDirectory, Logger);

		RegisterListener<OnCustomHudClicked>(OnClick);
		RegisterListener<OnMapStart>(_ => HandleMapStart());
		RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
		{
			CCSPlayerController? player = @event.Userid;
			if (player != null && TryGetSlot(player, out _))
			{
				if (player.IsValid)
				{
					Hide(player);
				}
				_slots[player.Slot] = new Slot();
			}
			return HookResult.Continue;
		});
		AddTimer(1.0f, Watchdog, TimerFlags.REPEAT);
		RegisterEventHandler<EventPlayerTeam>((@event, info) =>
		{
			CCSPlayerController? player = @event.Userid;
			if (player != null && player.IsValid && TryGetSlot(player, out Slot slot) && slot.Shown)
			{
				Server.NextFrame(() =>
				{
					if (player.IsValid)
					{
						QueueRefresh(player);
					}
				});
			}
			return HookResult.Continue;
		});
	}

	public override void OnAllPluginsLoaded(bool hotReload)
	{
		RegisterPainter();
		if (!_bridge.Available)
		{
			Logger.LogWarning("WeaponSkins is not loaded. The locker stays idle until it is.");
		}
		else
		{
			string version = _bridge.Version() ?? "?";
			if (TestedWeaponSkins.Contains(version))
			{
				Logger.LogInformation("WeaponSkins {Version} found. It is used as it is, nothing in it is changed.", version);
			}
			else
			{
				Logger.LogInformation("WeaponSkins {Version} found. This PMM_WeaponSkins was tried with WeaponSkins {Tested}. A menu it does not know is drawn as plain cards and named in the log.", version, string.Join(" and ", TestedWeaponSkins));
			}
			if (_bridge.MenuStyle() is int style && style != 3)
			{
				Logger.LogWarning("WeaponSkins has Menu_Style {Style}. Set \"Menu_Style\": 3 in configs/plugins/WeaponSkins/WeaponSkins.json to get this locker.", style);
			}
		}
		AddTimer(5.0f, CheckAddons);
	}

	private void CheckAddons()
	{
		try
		{
			ConVar? clients = ConVar.Find("mm_client_extra_addons");
			ConVar? server = ConVar.Find("mm_extra_addons");
			if (clients == null && server == null)
			{
				Logger.LogWarning("MultiAddonManager is not loaded (the convars mm_client_extra_addons and mm_extra_addons do not exist). Without it players do not get the Workshop addon with the locker files and the locker shows nothing.");
				return;
			}
			string clientList = (clients?.StringValue ?? "").Trim();
			string serverList = (server?.StringValue ?? "").Trim();
			if (clientList.Length == 0 && serverList.Length == 0)
			{
				Logger.LogWarning("mm_client_extra_addons and mm_extra_addons are both empty. Put the id of the Workshop addon with the locker files into mm_client_extra_addons, players get the files when they connect. (Ignore this if the files come with your Workshop map or are added through the MultiAddonManager API.)");
			}
			else
			{
				Logger.LogInformation("mm_client_extra_addons is \"{Clients}\", mm_extra_addons is \"{Server}\". The id of the Workshop addon with the locker files has to be in one of them, and only the newest copy of that addon. Type both names in the server console to read them again.", clientList, serverList);
			}
		}
		catch (Exception exception)
		{
			Logger.LogDebug(exception, "mm_client_extra_addons could not be read.");
		}
	}

	private void CheckArt()
	{
		if (_items.IsEmpty)
		{
			return;
		}
		int key = _bridge.CatalogKey();
		if (key < 0 || key == _artKey)
		{
			return;
		}
		_artKey = key;
		CatalogContents? catalog = _bridge.CatalogContents();
		if (catalog == null)
		{
			return;
		}
		ArtGaps gaps = ArtCheck.Find(_items, catalog);
		if (gaps.Total == 0)
		{
			Logger.LogInformation("All {Skins} skins, {Stickers} stickers, {Agents} agents, {Music} music kits and {Pins} pins of WeaponSkins have a picture in the locker files.",
				catalog.Skins.Count, catalog.Stickers.Count, catalog.Agents.Count, catalog.Music.Count, catalog.Pins.Count);
			return;
		}
		Logger.LogWarning("{Total} items of WeaponSkins have no picture in the locker files (skins {Skins}, stickers {Stickers}, agents {Agents}, music kits {Music}, pins {Pins}; for example {Example}). They are drawn without a picture. Run tools/gen_art.py --download <api.base_url of WeaponSkins.json> (or give it the Data folder of WeaponSkins), copy ws_items.json next to the DLL, restart, and rebuild and publish the addon.",
			gaps.Total, gaps.Skins, gaps.Stickers, gaps.Agents, gaps.Music, gaps.Pins, gaps.Example);
	}

	private void RegisterPainter()
	{
		if (_painterAdded)
		{
			return;
		}
		try
		{
			_registry = _paint.Get();
			if (_registry == null)
			{
				Logger.LogWarning("PanoramaMenuManagerCS2 (MenuManagerCore) is not loaded, so the locker cannot register on pmm:paint.");
				return;
			}
			_registry.Add(this);
			_painterAdded = true;
			Logger.LogInformation("PanoramaMenuManager_WeaponSkins registered on pmm:paint.");
		}
		catch (Exception exception)
		{
			Logger.LogError(exception, "pmm:paint is not available, so WeaponSkins menus stay on the normal Panorama list.");
		}
	}

	public override void Unload(bool hotReload)
	{
		_registry?.Remove(this);
		_painterAdded = false;
		for (int i = 0; i < MaxSlots; i++)
		{
			Slot slot = _slots[i];
			slot.Token++;
			if (slot.Open)
			{
				CCSPlayerController? player = Utilities.GetPlayerFromSlot(i);
				if (player != null && player.IsValid)
				{
					Hide(player);
				}
			}
		}
		CCSCustomHudLayout? layout = _layout;
		_layout = null;
		if (layout != null && layout.IsValid)
		{
			layout.Remove();
		}
	}

	private void HandleMapStart()
	{
		_layout = null;
		for (int i = 0; i < MaxSlots; i++)
		{
			_slots[i] = new Slot();
		}
	}

	private void Watchdog()
	{
		for (int i = 0; i < MaxSlots; i++)
		{
			if (!_slots[i].Open)
			{
				continue;
			}
			CCSPlayerController? player = Utilities.GetPlayerFromSlot(i);
			if (player == null || !player.IsValid)
			{
				_slots[i] = new Slot();
			}
			else if (_bridge.Peek(player) == null)
			{
				Hide(player);
			}
		}
	}

	private bool TryGetSlot(CCSPlayerController? player, out Slot slot)
	{
		if (player != null && player.Slot >= 0 && player.Slot < MaxSlots)
		{
			slot = _slots[player.Slot];
			return true;
		}
		slot = null!;
		return false;
	}


	public bool TryPaint(CCSPlayerController player, IMenu menu)
	{
		if (!player.IsValid || player.IsBot || !TryGetSlot(player, out Slot slot))
		{
			return false;
		}
		IMenuApi? api = _menus.Get();
		if (api != null && api.GetSelectedMenu(player) == MenuType.PanoramaWasdMenu)
		{
			return false;
		}
		MenuSnapshot? snapshot = _bridge.Read(player, menu);
		if (snapshot == null)
		{
			return false;
		}
		CheckArt();
		if (!EnsureLayout())
		{
			if (!_layoutWarned)
			{
				_layoutWarned = true;
				Logger.LogWarning("The locker layout was not created. WeaponSkins uses the normal Panorama list.");
			}
			return false;
		}

		slot.PaintSeq++;
		slot.Menu = menu;
		Note(slot, snapshot);
		slot.Open = true;
		ContinueStickerSlot(player, slot, snapshot);
		if (slot.Shown)
		{
			QueueRefresh(player);
			return true;
		}
		int token = ++slot.Token;
		Later(Config.OpenDelay, () =>
		{
			if (slot.Token == token && player.IsValid && slot.Open)
			{
				Show(player);
				CaptureInput(player, token);
			}
		});
		return true;
	}

	public void Close(CCSPlayerController player)
	{
		if (!TryGetSlot(player, out Slot slot))
		{
			return;
		}
		int seq = slot.PaintSeq;
		Server.NextFrame(() =>
		{
			if (player.IsValid && slot.PaintSeq == seq)
			{
				Hide(player);
			}
		});
	}

	public bool IsOpen(CCSPlayerController player)
	{
		return TryGetSlot(player, out Slot slot) && slot.Open;
	}

	private static void Note(Slot slot, MenuSnapshot snapshot)
	{
		string key = snapshot.Depth + "|" + snapshot.Kind + "|" + snapshot.Title + "|" + snapshot.Count
			+ "|" + snapshot.Tag(0) + "|" + snapshot.Tag(snapshot.Count - 1);
		if (snapshot.Depth <= 1)
		{
			if (!string.Equals(key, slot.PageKey, StringComparison.Ordinal))
			{
				slot.ForgetNavigation();
			}
			slot.RootKind = snapshot.Kind;
		}
		while (slot.Trail.Count > snapshot.Depth - 1)
		{
			slot.Trail.RemoveAt(slot.Trail.Count - 1);
		}
		while (slot.Trail.Count < snapshot.Depth - 1)
		{
			slot.Trail.Add("");
		}
		slot.Trail.Add(snapshot.Title);

		if (!string.Equals(key, slot.PageKey, StringComparison.Ordinal))
		{
			if (slot.PageKey != null)
			{
				slot.Pages[slot.PageKey] = slot.Page;
			}
			slot.PageKey = key;
			slot.Page = slot.Pages.TryGetValue(key, out int page) ? page : 0;
		}
	}


	private void OnClick(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId)
	{
		CCSCustomHudLayout? mine = _layout;
		if (mine == null || layout.Handle != mine.Handle || !TryGetSlot(player, out Slot slot) || !slot.Open)
		{
			return;
		}
		if (!buttonId.StartsWith("ws-", StringComparison.Ordinal))
		{
			return;
		}
		long now = Environment.TickCount64;
		if (now < slot.NextClick)
		{
			return;
		}
		slot.NextClick = now + ClickCooldownMs;

		switch (buttonId)
		{
			case "ws-close":
				_bridge.Close(player);
				Hide(player);
				return;
			case "ws-back":
				_bridge.Back(player);
				AfterAction(player);
				return;
			case "ws-prev":
				Turn(player, slot, slot.Page - 1);
				return;
			case "ws-next":
				Turn(player, slot, slot.Page + 1);
				return;
			case "ws-pv-all":
				AllSlots(player, slot);
				return;
		}

		if (buttonId.StartsWith("ws-cat-", StringComparison.Ordinal))
		{
			OpenSection(player, buttonId["ws-cat-".Length..]);
		}
		else if (buttonId.StartsWith("ws-num-", StringComparison.Ordinal) && int.TryParse(buttonId["ws-num-".Length..], out int number))
		{
			Turn(player, slot, slot.PagerStart + number);
		}
		else if (buttonId.StartsWith("ws-card-", StringComparison.Ordinal) && int.TryParse(buttonId["ws-card-".Length..], out int card))
		{
			Activate(player, slot, slot.Index(CardPosition(slot.Page, PageSize, card)));
		}
		else if (buttonId.StartsWith("ws-row-", StringComparison.Ordinal) && int.TryParse(buttonId["ws-row-".Length..], out int row))
		{
			Activate(player, slot, slot.Index(row));
		}
		else if (buttonId.StartsWith("ws-pvst-", StringComparison.Ordinal) && int.TryParse(buttonId["ws-pvst-".Length..], out int square))
		{
			OpenStickerSlot(player, slot, square);
		}
	}

	private void Activate(CCSPlayerController player, Slot slot, int index)
	{
		IMenu? menu = slot.Menu;
		if (menu == null || index < 0 || index >= menu.MenuOptions.Count)
		{
			return;
		}
		ChatMenuOption option = menu.MenuOptions[index];
		if (option.Disabled || option.OnSelect == null)
		{
			return;
		}
		int token = slot.Token;
		Server.NextFrame(() =>
		{
			if (!player.IsValid || !slot.Open || slot.Token != token)
			{
				return;
			}
			option.OnSelect.Invoke(player, option);
			AfterAction(player);
		});
	}

	private void OpenStickerSlot(CCSPlayerController player, Slot slot, int square)
	{
		if (square < 0 || square >= Layout.StickerSlots)
		{
			return;
		}
		MenuSnapshot? snapshot = _bridge.Peek(player);
		if (snapshot == null)
		{
			return;
		}
		if (snapshot.Kind == Kind.StickerSlots)
		{
			Activate(player, slot, CardFactory.IndexOfTag(snapshot, "slot:" + square));
		}
		else if (snapshot.Kind == Kind.Customize)
		{
			int index = CardFactory.IndexOfTag(snapshot, "stickers");
			if (index >= 0)
			{
				slot.PendingSquare = square;
				slot.PendingUntil = Environment.TickCount64 + PendingMs;
				Activate(player, slot, index);
			}
		}
	}

	private void ContinueStickerSlot(CCSPlayerController player, Slot slot, MenuSnapshot snapshot)
	{
		if (slot.PendingSquare < 0)
		{
			return;
		}
		int square = slot.PendingSquare;
		slot.PendingSquare = -1;
		if (snapshot.Kind == Kind.StickerSlots && Environment.TickCount64 <= slot.PendingUntil)
		{
			Activate(player, slot, CardFactory.IndexOfTag(snapshot, "slot:" + square));
		}
	}

	private void AllSlots(CCSPlayerController player, Slot slot)
	{
		MenuSnapshot? snapshot = _bridge.Peek(player);
		if (snapshot != null && snapshot.Kind == Kind.StickerSlots)
		{
			Activate(player, slot, CardFactory.IndexOfTag(snapshot, "slot-all"));
		}
	}

	private void OpenSection(CCSPlayerController player, string key)
	{
		string command = _bridge.Command(key == "weapons" ? "skins" : key);
		Server.NextFrame(() =>
		{
			if (player.IsValid)
			{
				player.ExecuteClientCommandFromServer("css_" + command);
			}
		});
	}

	private void Turn(CCSPlayerController player, Slot slot, int target)
	{
		MenuSnapshot? snapshot = _bridge.Peek(player);
		if (snapshot == null)
		{
			return;
		}
		slot.Page = PageMath(snapshot.Count, target, PageSize).Page;
		if (slot.PageKey != null)
		{
			slot.Pages[slot.PageKey] = slot.Page;
		}
		Apply(player);
	}

	private void AfterAction(CCSPlayerController player)
	{
		Server.NextFrame(() =>
		{
			if (!player.IsValid || !TryGetSlot(player, out Slot slot) || !slot.Open)
			{
				return;
			}
			if (_bridge.Peek(player) == null)
			{
				Hide(player);
			}
			else
			{
				QueueRefresh(player);
			}
		});
	}

	private void QueueRefresh(CCSPlayerController player)
	{
		if (!TryGetSlot(player, out Slot slot) || slot.RefreshQueued)
		{
			return;
		}
		slot.RefreshQueued = true;
		Server.NextFrame(() =>
		{
			slot.RefreshQueued = false;
			if (player.IsValid && slot.Open && slot.Shown)
			{
				Apply(player);
			}
		});
	}


	private void Show(CCSPlayerController player)
	{
		CCSCustomHudLayout? layout = _layout;
		if (layout == null || !layout.IsValid || !TryGetSlot(player, out Slot slot))
		{
			return;
		}
		slot.Shown = true;
		Apply(player);
		if (slot.Shown)
		{
			layout.SetHasClassForPlayer(player, "ws-root", "ws-hidden", false);
		}
	}

	private void Hide(CCSPlayerController player)
	{
		if (TryGetSlot(player, out Slot slot))
		{
			slot.Token++;
			slot.Open = false;
			slot.Shown = false;
			slot.Menu = null;
			slot.CaptureArmed = false;
			slot.Captured = false;
			slot.Prompting = false;
			slot.Map = null;
			slot.MapMenu = null;
			slot.PendingSquare = -1;
			slot.ForgetNavigation();
			slot.FlipToken++;
			slot.GridKey = null;
			slot.GridCards = 0;
		}
		CCSCustomHudLayout? layout = _layout;
		if (layout != null && layout.IsValid && player.IsValid)
		{
			layout.SetInputCaptureEnabled(player, false);
			if (TryGetSlot(player, out Slot hidden))
			{
				new Sender(layout, player, hidden).Class(Root, "ws-flip", false);
			}
			layout.SetHasClassForPlayer(player, "ws-root", "ws-hidden", true);
		}
	}

	private void CaptureInput(CCSPlayerController player, int token)
	{
		Later(Config.InputDelay, () =>
		{
			if (TryGetSlot(player, out Slot slot) && slot.Token == token && slot.Shown && player.IsValid)
			{
				slot.CaptureArmed = true;
				SyncCapture(player, slot);
			}
		});
	}

	private void SyncCapture(CCSPlayerController player, Slot slot)
	{
		CCSCustomHudLayout? layout = _layout;
		if (layout == null || !layout.IsValid)
		{
			return;
		}
		bool want = slot.Shown && slot.CaptureArmed && !slot.Prompting;
		if (slot.Captured == want)
		{
			return;
		}
		slot.Captured = want;
		layout.SetInputCaptureEnabled(player, want);
	}

	private bool EnsureLayout()
	{
		CCSCustomHudLayout? layout = _layout;
		if (layout != null && layout.IsValid)
		{
			return true;
		}
		CCSCustomHudLayout? created = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
		if (created == null || created.Handle == IntPtr.Zero)
		{
			return false;
		}
		created.StrLayout = LayoutPath;
		created.DispatchSpawn();
		if (!created.IsValid)
		{
			return false;
		}
		_layout = created;
		foreach (Slot slot in _slots)
		{
			slot.ForgetSent();
		}
		return true;
	}

	private void Later(float seconds, Action callback)
	{
		if (seconds <= 0f)
		{
			Server.NextFrame(callback);
		}
		else
		{
			AddTimer(seconds, callback);
		}
	}
}
