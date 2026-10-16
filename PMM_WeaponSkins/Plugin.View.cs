using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;

namespace PMM_WeaponSkins;

public partial class Plugin
{
	private const string Root = "ws-root";

	private sealed class Sender
	{
		private readonly CCSCustomHudLayout _layout;
		private readonly CCSPlayerController _player;
		private readonly Slot _slot;

		public CCSPlayerController Player => _player;

		public Sender(CCSCustomHudLayout layout, CCSPlayerController player, Slot slot)
		{
			_layout = layout;
			_player = player;
			_slot = slot;
		}

		public void Class(string panel, string cls, bool on)
		{
			string key = panel + "\u0001" + cls;
			if (_slot.SentClass.TryGetValue(key, out bool current) && current == on)
			{
				return;
			}
			_slot.SentClass[key] = on;
			_layout.SetHasClassForPlayer(_player, panel, cls, on);
		}

		public void Var(string panel, string name, string value)
		{
			string key = panel + "\u0001" + name;
			if (_slot.SentVar.TryGetValue(key, out string? current) && current == value)
			{
				return;
			}
			_slot.SentVar[key] = value;
			_layout.SetDialogVariableStringForPlayer(_player, panel, name, value);
		}

		public void Art(string panel, ref string? previous, string? cls)
		{
			if (!string.IsNullOrEmpty(previous) && previous != cls)
			{
				Class(panel, previous, false);
			}
			if (!string.IsNullOrEmpty(cls))
			{
				Class(panel, cls, true);
			}
			previous = cls;
		}
	}


	private void Apply(CCSPlayerController player)
	{
		CCSCustomHudLayout? layout = _layout;
		if (layout == null || !layout.IsValid || !TryGetSlot(player, out Slot slot))
		{
			return;
		}
		MenuSnapshot? snapshot = _bridge.Peek(player);
		if (snapshot == null)
		{
			Hide(player);
			return;
		}

		bool ct = player.Team == CsTeam.CounterTerrorist;
		Frame frame = new()
		{
			Snap = snapshot,
			Loadout = _bridge.Loadout(player) ?? new LoadoutSnapshot(),
			Ct = ct,
			Language = Language,
			Items = _items,
			Catalog = _bridge,
			Disabled = index => IsDisabled(slot, index)
		};
		Sender send = new(layout, player, slot);

		bool board = CardFactory.IsBoard(snapshot);
		send.Class(Root, "view-grid", !board);
		send.Class(Root, "view-board", board);
		send.Class(Root, "team-t", !ct);
		send.Class(Root, "team-ct", ct);

		send.Var(Root, "t_back", frame.T("back"));
		send.Var(Root, "t_all", frame.T("row.all"));
		send.Var(Root, "t_equipped", frame.T("equipped"));
		send.Var(Root, "t_prompt", frame.T("prompt.title"));
		send.Var(Root, "t_prompt_hint", frame.T("prompt.hint"));

		string current = CardFactory.RailKey(slot.RootKind.Length > 0 ? slot.RootKind : snapshot.Kind);
		for (int i = 0; i < Layout.Rail.Length; i++)
		{
			string key = Layout.Rail[i];
			RailInfo info = CardFactory.MakeRail(frame, key);
			send.Class(Root, "cat-" + key, key == current);
			send.Var(Root, "cat_" + key, frame.T("cat." + key));
			send.Var(Root, "sub_" + key, info.Sub);
			send.Art("ws-sw-" + key, ref slot.RailArt[i], info.Art);
		}

		send.Var(Root, "ws_title", snapshot.Title);
		send.Var(Root, "ws_crumb", Crumb(slot, frame));

		bool prompting = snapshot.Prompt != null;
		slot.Prompting = prompting;
		send.Class(Root, "prompting", prompting);
		if (prompting)
		{
			send.Var(Root, "prompt_text", snapshot.Prompt!);
		}

		if (board)
		{
			PaintBoard(send, frame, slot);
		}
		else
		{
			PaintGrid(send, frame, slot);
		}
		SyncCapture(player, slot);
	}

	private static bool IsDisabled(Slot slot, int index)
	{
		IMenu? menu = slot.Menu;
		return menu != null && index >= 0 && index < menu.MenuOptions.Count && menu.MenuOptions[index].Disabled;
	}

	private static string Crumb(Slot slot, Frame frame)
	{
		List<string> parents = new();
		for (int i = 0; i < slot.Trail.Count - 1; i++)
		{
			if (slot.Trail[i].Length > 0)
			{
				parents.Add(slot.Trail[i]);
			}
		}
		return parents.Count == 0 ? frame.T("crumb.root") : string.Join(frame.T("crumb.sep"), parents);
	}


	private void PaintGrid(Sender send, Frame frame, Slot slot)
	{
		int count = frame.Snap.Count;
		int[]? order = CardFactory.GridOrder(frame);
		slot.Map = order;
		slot.MapMenu = slot.Menu;
		(int pages, int page, int first, int onPage) = PageMath(count, slot.Page, PageSize);
		slot.Page = page;

		if (NoteGrid(slot, slot.Page, onPage))
		{
			StartFlip(send, slot);
		}
		int lastRow = onPage > 0 ? (onPage - 1) / Layout.CardsPerRow : -1;

		for (int i = 0; i < Layout.CardCount; i++)
		{
			Card? card = i < onPage ? CardFactory.MakeCard(frame, order != null ? order[first + i] : first + i) : null;
			RenderCard(send, slot, i, card, card == null && i / Layout.CardsPerRow == lastRow, frame.Ct);
		}

		send.Class(Root, "show-stickers", false);
		send.Var(Root, "ws_hint", count == 0 ? frame.T("hint.empty") : count == 1 ? frame.T("hint.item") : frame.T("hint.items", count));
		FillPager(send, slot, pages);
	}

	private static void RenderCard(Sender send, Slot slot, int i, Card? card, bool ghost, bool ct)
	{
		string panel = "ws-card-" + i;
		send.Class(panel, "ws-off", card == null && !ghost);
		send.Class(panel, "ws-ghost", ghost);
		if (card == null)
		{
			return;
		}
		send.Var(panel, "cn" + i, card.Name);
		send.Var(panel, "cs" + i, card.Sub);
		send.Art("ws-cbase-" + i, ref slot.CardBase[i], card.Base);
		send.Art("ws-cart-" + i, ref slot.CardArt[i], card.Art);
		send.Class(panel, "ws-noart", string.IsNullOrEmpty(card.Art) && string.IsNullOrEmpty(card.Base));
		send.Class(panel, "ws-dis", card.Disabled);
		send.Class(panel, "ws-plain", card.Plain);
		send.Class(panel, "is-eq", !card.Plain && (ct ? card.EqCt : card.EqT));
		send.Class(panel, "is-stock", card.Stock);
		send.Class(panel, "on-t", !card.Plain && card.EqT);
		send.Class(panel, "on-ct", !card.Plain && card.EqCt);
		send.Art(panel, ref slot.CardRarity[i], card.Rarity > 0 && !card.Stock ? "rar-" + card.Rarity : null);
	}

	private static void FillPager(Sender send, Slot slot, int pages)
	{
		int page = slot.Page;
		int start = pages > Layout.PagerButtons ? Math.Clamp(page - Layout.PagerButtons / 2, 0, pages - Layout.PagerButtons) : 0;
		slot.PagerStart = start;
		for (int i = 0; i < Layout.PagerButtons; i++)
		{
			int number = start + i;
			bool shown = pages > 1 && number < pages;
			string panel = "ws-num-" + i;
			send.Class(panel, "ws-off", !shown);
			send.Class(panel, "is-cur", shown && number == page);
			send.Var(panel, "num" + i, (number + 1).ToString());
		}
		send.Class("ws-prev", "ws-dim", page <= 0);
		send.Class("ws-next", "ws-dim", page >= pages - 1);
		send.Class(Root, "multi-page", pages > 1);
		send.Class(Root, "single-page", pages <= 1);
	}

	private static (int Pages, int Page, int First, int OnPage) PageMath(int count, int page, int size)
	{
		int pages = Math.Max(1, (count + size - 1) / size);
		page = Math.Clamp(page, 0, pages - 1);
		int first = page * size;
		return (pages, page, first, Math.Clamp(count - first, 0, size));
	}

	private static int CardPosition(int page, int size, int card) => page * size + card;

	private static bool NoteGrid(Slot slot, int page, int cards)
	{
		string key = slot.PageKey + "#" + page;
		bool flip = NeedsFlip(slot, key);
		slot.GridKey = key;
		slot.GridCards = cards;
		return flip;
	}

	private static bool NeedsFlip(Slot slot, string gridKey)
	{
		return slot.GridKey != null && slot.GridKey != gridKey && slot.GridCards > Layout.WindowRows * Layout.CardsPerRow;
	}

	private void StartFlip(Sender send, Slot slot)
	{
		float delay = Config.PageResetDelay;
		if (delay <= 0f)
		{
			return;
		}
		CCSPlayerController player = send.Player;
		send.Class(Root, "ws-flip", true);
		int token = ++slot.FlipToken;
		Later(delay, () =>
		{
			CCSCustomHudLayout? layout = _layout;
			if (slot.FlipToken != token || layout == null || !layout.IsValid || !player.IsValid)
			{
				return;
			}
			new Sender(layout, player, slot).Class(Root, "ws-flip", false);
		});
	}


	private void PaintBoard(Sender send, Frame frame, Slot slot)
	{
		Preview preview = CardFactory.MakePreview(frame);
		send.Var(Root, "pv_title", preview.Title);
		send.Var(Root, "pv_sub", preview.Sub);
		for (int k = 0; k < Layout.PreviewLines; k++)
		{
			send.Var(Root, "pv_l" + k, preview.Lines[k]);
		}
		send.Art("ws-pvbase", ref slot.PreviewBase, preview.Base);
		send.Art("ws-pvart", ref slot.PreviewArt, preview.Art);
		send.Art("ws-pv", ref slot.PreviewRarity, preview.Rarity > 0 ? "rar-" + preview.Rarity : null);
		send.Class(Root, "show-stickers", preview.ShowStickers);
		send.Class(Root, "st-live", preview.ShowStickers && preview.Live);
		send.Class(Root, "st-all", preview.ShowStickers && preview.All);
		for (int k = 0; k < Layout.StickerSlots; k++)
		{
			int id = preview.Stickers[k];
			send.Art("ws-pvst-" + k, ref slot.PreviewSticker[k], id > 0 ? Art.Sticker(id) : null);
			send.Class("ws-pvst-" + k, "has-art", id > 0);
		}

		int[] rows = CardFactory.BoardRows(frame);
		slot.Map = rows;
		slot.MapMenu = slot.Menu;
		for (int i = 0; i < Layout.RowCount; i++)
		{
			Row? row = i < rows.Length ? CardFactory.MakeRow(frame, rows[i]) : null;
			string panel = "ws-row-" + i;
			send.Class(panel, "ws-off", row == null);
			if (row == null)
			{
				continue;
			}
			send.Var(panel, "rn" + i, row.Name);
			send.Var(panel, "rv" + i, row.Value);
			send.Class(panel, "has-val", row.Value.Length > 0);
			send.Class(panel, "is-center", row.Center);
			send.Art("ws-rart-" + i, ref slot.RowArt[i], row.Art);
			send.Class(panel, "has-art", !string.IsNullOrEmpty(row.Art));
			send.Class(panel, "is-on", row.On == true);
			send.Class(panel, "is-off", row.On == false);
			send.Class(panel, "is-eq", row.Eq);
			send.Class(panel, "ws-dis", row.Disabled);
		}

		send.Var(Root, "ws_hint", frame.T("hint.board"));
		slot.Page = 0;
		FillPager(send, slot, 1);
	}
}
