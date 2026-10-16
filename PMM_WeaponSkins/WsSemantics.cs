using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PMM_WeaponSkins;

internal sealed class MenuInfo
{
	public string Kind = "";

	public string Scope = "";

	public string Builder = "";

	public int[]? Order;
	public bool OrderDone;
	public int OrderKey;

	private readonly IReadOnlyList<object> _items;
	private readonly string?[] _tags;
	private readonly bool[] _known;

	public MenuInfo(IReadOnlyList<object> items)
	{
		_items = items;
		_tags = new string?[items.Count];
		_known = new bool[items.Count];
	}

	public string? Tag(int index)
	{
		if (index < 0 || index >= _tags.Length || Kind.Length == 0)
		{
			return null;
		}
		if (!_known[index])
		{
			try
			{
				_tags[index] = WsSemantics.TagOf(Kind, index, _items.Count, _items[index]);
			}
			catch (Exception)
			{
				_tags[index] = null;
			}
			_known[index] = true;
		}
		return _tags[index];
	}
}

internal sealed class Act
{
	public string Direct = "";

	public readonly HashSet<string> Calls = new(StringComparer.Ordinal);

	public readonly HashSet<int> Ints = new();
	public readonly HashSet<string> Strings = new(StringComparer.Ordinal);

	public readonly Dictionary<string, object?> Captured = new(StringComparer.Ordinal);

	public bool Has(params string[] names)
	{
		foreach (string name in names)
		{
			if (Calls.Contains(name))
			{
				return true;
			}
		}
		return false;
	}

	public object? Of(string typeName)
	{
		foreach (object? value in Captured.Values)
		{
			if (value != null && value.GetType().Name == typeName)
			{
				return value;
			}
		}
		return null;
	}

	public string? Str(string name) => Captured.TryGetValue(name, out object? value) ? value as string : null;

	public int? Int(string name) => Captured.TryGetValue(name, out object? value) && value is int number ? number : null;
}

internal static class WsSemantics
{
	private const string MenusType = "WeaponSkins.SkinMenus";
	private const string RendererType = "WeaponSkins.MenuRenderer";

	private static readonly Dictionary<string, string> Builders = new(StringComparer.Ordinal)
	{
		["SkinsRoot"] = Kind.Categories,
		["CategoryMenu"] = Kind.Weapons,
		["PaintList"] = Kind.Skins,
		["Customize"] = Kind.Customize,
		["WearMenu"] = Kind.Wear,
		["NameTagMenu"] = Kind.NameTag,
		["SeedMenu"] = Kind.Seed,
		["KnifeRoot"] = Kind.Knives,
		["GlovesRoot"] = Kind.Gloves,
		["GlovePaintList"] = Kind.GloveSkins,
		["GloveCustomize"] = Kind.GloveCustomize,
		["GloveWearMenu"] = Kind.GloveWear,
		["GloveSeedMenu"] = Kind.GloveSeed,
		["OpenTarget"] = Kind.ApplyTo,
		["AgentsRoot"] = Kind.AgentTeams,
		["AgentList"] = Kind.AgentFactions,
		["AgentFactionList"] = Kind.Agents,
		["MusicRoot"] = Kind.Music,
		["PinsRoot"] = Kind.PinGroups,
		["PinGroupList"] = Kind.Pins,
		["StickerSlots"] = Kind.StickerSlots,
		["StickerRemoveMenu"] = Kind.StickerRemove,
		["StickerSourceMenu"] = Kind.StickerSources,
		["StickerEventMenu"] = Kind.StickerEvents,
		["StickerTypeMenu"] = Kind.StickerTypes,
		["StickerAudienceMenu"] = Kind.StickerAudience,
		["StickerSelectionList"] = Kind.Stickers,
		["FilteredStickerList"] = Kind.StickerSearch,
		["StickerLoadingMenu"] = Kind.Loading
	};

	private static readonly HashSet<string> StickerBuilders = new(StringComparer.Ordinal)
	{
		"StickerSourceMenu", "StickerEventMenu", "StickerTypeMenu", "StickerAudienceMenu", "StickerSelectionList", "FilteredStickerList"
	};


	private sealed class Scan
	{
		public readonly HashSet<string> Calls = new(StringComparer.Ordinal);
		public readonly HashSet<int> Ints = new();
		public readonly HashSet<string> Strings = new(StringComparer.Ordinal);
		public string Creator = "";
	}

	private static readonly OpCode[] OneByte = new OpCode[256];
	private static readonly OpCode[] TwoByte = new OpCode[256];
	private static readonly ConditionalWeakTable<MethodBase, Scan> Scans = new();
	private static readonly Regex Lambda = new(@"^<(?<creator>[^>]+)>b__", RegexOptions.Compiled);

	static WsSemantics()
	{
		foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (field.GetValue(null) is not OpCode code)
			{
				continue;
			}
			int low = (ushort)code.Value & 0xFF;
			if (code.Size == 1)
			{
				OneByte[low] = code;
			}
			else
			{
				TwoByte[low] = code;
			}
		}
	}

	private static bool IsGenerated(MethodBase method) => method.Name.Length > 0 && method.Name[0] == '<';

	private static Scan ScanOf(MethodBase method, int depth = 0)
	{
		if (Scans.TryGetValue(method, out Scan? known))
		{
			return known;
		}
		Scan scan = new();
		Scans.Add(method, scan);
		Match match = Lambda.Match(method.Name);
		if (match.Success)
		{
			scan.Creator = match.Groups["creator"].Value;
		}
		try
		{
			ReadIl(method, scan, depth);
		}
		catch (Exception)
		{
		}
		return scan;
	}

	private static void ReadIl(MethodBase method, Scan scan, int depth)
	{
		byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
		if (il == null)
		{
			return;
		}
		Module module = method.Module;
		Type[]? typeArguments = method.DeclaringType is { IsGenericType: true } ? method.DeclaringType.GetGenericArguments() : null;
		Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

		int i = 0;
		while (i < il.Length)
		{
			OpCode code = il[i] == 0xFE ? TwoByte[il[i + 1]] : OneByte[il[i]];
			i += code.Size;
			if (code.Name == null)
			{
				return;
			}
			int operand = i;
			switch (code.OperandType)
			{
				case OperandType.InlineNone:
					break;
				case OperandType.ShortInlineBrTarget:
				case OperandType.ShortInlineI:
				case OperandType.ShortInlineVar:
					i += 1;
					break;
				case OperandType.InlineVar:
					i += 2;
					break;
				case OperandType.InlineI8:
				case OperandType.InlineR:
					i += 8;
					break;
				case OperandType.InlineSwitch:
					i += 4 + 4 * BitConverter.ToInt32(il, i);
					break;
				default:
					i += 4;
					break;
			}

			if (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj || code == OpCodes.Ldftn || code == OpCodes.Ldvirtftn)
			{
				MethodBase? target = Resolve(module, BitConverter.ToInt32(il, operand), typeArguments, methodArguments);
				if (target != null)
				{
					Note(target, code == OpCodes.Ldftn || code == OpCodes.Ldvirtftn, scan, depth);
				}
			}
			else if (code == OpCodes.Ldstr)
			{
				scan.Strings.Add(module.ResolveString(BitConverter.ToInt32(il, operand)));
			}
			else if (code == OpCodes.Ldc_I4)
			{
				scan.Ints.Add(BitConverter.ToInt32(il, operand));
			}
			else if (code == OpCodes.Ldc_I4_S)
			{
				scan.Ints.Add((sbyte)il[operand]);
			}
			else if (code == OpCodes.Ldc_I4_M1)
			{
				scan.Ints.Add(-1);
			}
			else if (code.Value >= OpCodes.Ldc_I4_0.Value && code.Value <= OpCodes.Ldc_I4_8.Value)
			{
				scan.Ints.Add(code.Value - OpCodes.Ldc_I4_0.Value);
			}
		}
	}

	private static MethodBase? Resolve(Module module, int token, Type[]? typeArguments, Type[]? methodArguments)
	{
		try
		{
			return module.ResolveMethod(token, typeArguments, methodArguments);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static void Note(MethodBase target, bool reference, Scan scan, int depth)
	{
		string owner = target.DeclaringType?.FullName ?? "";
		if (IsGenerated(target))
		{
			if (reference && depth < 3)
			{
				Scan inner = ScanOf(target, depth + 1);
				scan.Calls.UnionWith(inner.Calls);
				scan.Ints.UnionWith(inner.Ints);
				scan.Strings.UnionWith(inner.Strings);
			}
		}
		else if (owner == MenusType)
		{
			scan.Calls.Add(target.Name);
		}
		else if (owner == RendererType)
		{
			scan.Calls.Add("Renderer." + target.Name);
		}
	}


	public static Act Analyze(Delegate? handler)
	{
		Act act = new();
		if (handler == null)
		{
			return act;
		}
		MethodInfo method = handler.Method;
		if (IsGenerated(method))
		{
			Scan scan = ScanOf(method);
			act.Calls.UnionWith(scan.Calls);
			act.Ints.UnionWith(scan.Ints);
			act.Strings.UnionWith(scan.Strings);
		}
		else if (method.DeclaringType?.FullName == MenusType)
		{
			act.Direct = method.Name;
			act.Calls.Add(method.Name);
		}
		Capture(handler.Target, act.Captured, 0);
		return act;
	}

	private static void Capture(object? target, Dictionary<string, object?> into, int depth)
	{
		if (target == null || depth > 4 || target.GetType().FullName == MenusType)
		{
			return;
		}
		foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			object? value;
			try
			{
				value = field.GetValue(target);
			}
			catch (Exception)
			{
				continue;
			}
			if (field.Name.StartsWith("CS$<>8__locals", StringComparison.Ordinal))
			{
				Capture(value, into, depth + 1);
			}
			else if (field.Name.StartsWith("<>4__this", StringComparison.Ordinal) || value is Delegate)
			{
				continue;
			}
			else
			{
				into.TryAdd(field.Name, value);
			}
		}
	}


	private static readonly ConditionalWeakTable<MethodBase, string> BuilderNames = new();
	private sealed class ItemFields
	{
		public FieldInfo? Select;
		public FieldInfo? Toggle;
	}

	private static readonly ConditionalWeakTable<Type, ItemFields> ItemTypes = new();

	private static ItemFields FieldsOf(object item)
	{
		Type type = item.GetType();
		if (!ItemTypes.TryGetValue(type, out ItemFields? fields))
		{
			fields = new ItemFields
			{
				Select = type.GetField("Select", BindingFlags.Public | BindingFlags.Instance),
				Toggle = type.GetField("Toggle", BindingFlags.Public | BindingFlags.Instance)
			};
			ItemTypes.Add(type, fields);
		}
		return fields;
	}

	private static Delegate? SelectOf(object item) => FieldsOf(item).Select?.GetValue(item) as Delegate;

	private static bool? ToggleOf(object item) => FieldsOf(item).Toggle?.GetValue(item) as bool?;

	private static string BuilderOf(Delegate? factory)
	{
		if (factory == null)
		{
			return "";
		}
		MethodInfo method = factory.Method;
		if (BuilderNames.TryGetValue(method, out string? known))
		{
			return known;
		}
		string name;
		if (!IsGenerated(method))
		{
			name = method.Name;
		}
		else
		{
			Scan scan = ScanOf(method);
			name = scan.Calls.FirstOrDefault(Builders.ContainsKey) ?? scan.Creator;
		}
		BuilderNames.AddOrUpdate(method, name);
		return name;
	}

	public static MenuInfo Describe(IReadOnlyList<object> items, Delegate? factory, Action<string>? unknown = null)
	{
		MenuInfo info = new(items);
		try
		{
			Fill(info, items, factory, unknown);
		}
		catch (Exception)
		{
			info.Kind = "";
			info.Scope = "";
		}
		return info;
	}

	private static void Fill(MenuInfo info, IReadOnlyList<object> items, Delegate? factory, Action<string>? unknown)
	{
		string builder = BuilderOf(factory);
		info.Builder = builder;
		if (builder.Length == 0 || !Builders.TryGetValue(builder, out string? kind))
		{
			if (builder.Length > 0)
			{
				unknown?.Invoke(builder);
			}
			return;
		}

		if (builder == "AgentsRoot")
		{
			for (int i = 0; i < Math.Min(items.Count, 3); i++)
			{
				Act act = Analyze(SelectOf(items[i]));
				if (act.Has("SetAgent", "AgentFactionList"))
				{
					kind = Kind.AgentFactions;
					break;
				}
			}
		}
		else if (StickerBuilders.Contains(builder) && items.Count == 1 && SelectOf(items[0]) == null)
		{
			kind = Kind.Loading;
		}
		info.Kind = kind;
		info.Scope = ScopeOf(kind, items);
	}

	private static string ScopeOf(string kind, IReadOnlyList<object> items)
	{
		if (kind is not (Kind.StickerEvents or Kind.StickerTypes or Kind.StickerAudience))
		{
			return "";
		}
		for (int i = 0; i < Math.Min(items.Count, 3); i++)
		{
			Delegate? select = SelectOf(items[i]);
			if (select == null)
			{
				continue;
			}
			Act act = Analyze(select);
			string? source = act.Str("source");
			string? group = act.Str("group");
			string? type = act.Str("type");
			switch (kind)
			{
				case Kind.StickerEvents when source != null:
					return source;
				case Kind.StickerTypes when source != null && group != null:
					return source + "|" + group;
				case Kind.StickerAudience when source != null && group != null && type != null:
					return source + "|" + group + "|" + type;
			}
		}
		return "";
	}


	public static string? TagOf(string kind, int index, int count, object item)
	{
		Delegate? select = SelectOf(item);
		if (select == null)
		{
			return null;
		}
		Act act = Analyze(select);
		bool? toggle = ToggleOf(item);
		switch (kind)
		{
			case Kind.Categories:
				return act.Str("name") is { } category ? "category:" + category : null;

			case Kind.Weapons:
				if (Prop(act.Of("KnifeDef"), "DefIndex") is { } knife)
				{
					return "knife:" + knife;
				}
				return act.Int("def") is { } weapon ? "weapon:" + weapon : null;

			case Kind.Skins:
			case Kind.GloveSkins:
				if (Prop(act.Of("PaintDef"), "Paint") is { } paint)
				{
					return "paint:" + paint;
				}
				return act.Has("SetPaint") ? "default" : null;

			case Kind.Customize:
			case Kind.GloveCustomize:
				if (toggle.HasValue)
				{
					return "stattrak";
				}
				if (act.Has("WearMenu", "GloveWearMenu"))
				{
					return "wear";
				}
				if (act.Has("PromptSeed", "PromptGloveSeed"))
				{
					return "seed";
				}
				if (act.Has("NameTagMenu", "PromptNameTag"))
				{
					return "nametag";
				}
				return act.Has("StickerSlots") ? "stickers" : null;

			case Kind.Wear:
			case Kind.GloveWear:
				return act.Captured.TryGetValue("wear", out object? wear) && wear is float amount
					? "wear:" + amount.ToString("0.00", CultureInfo.InvariantCulture)
					: "wear-custom";

			case Kind.NameTag:
				return act.Direct == "PromptNameTag" ? "nametag-change" : "nametag-remove";

			case Kind.Seed:
			case Kind.GloveSeed:
				return "seed";

			case Kind.Knives:
				return Prop(act.Of("KnifeDef"), "DefIndex") is { } listed ? "knife:" + listed : act.Has("SetKnife") ? "default" : null;

			case Kind.Gloves:
				if (act.Direct == "ResetGloves")
				{
					return "default";
				}
				return act.Int("def") is { } glove ? "glove:" + glove : null;

			case Kind.ApplyTo:
				return index == 0 ? "knife" : "gloves";

			case Kind.AgentTeams:
				return act.Ints.Contains(3) ? "team:ct" : act.Ints.Contains(2) ? "team:t" : index == 0 ? "team:ct" : "team:t";

			case Kind.AgentFactions:
				if (act.Has("SetAgent"))
				{
					return "default";
				}
				return act.Str("name") is { } faction ? "faction:" + faction : null;

			case Kind.Agents:
				return Prop(act.Of("AgentDef"), "Model") is { } agent ? "agent:" + agent : null;

			case Kind.Music:
				return Prop(act.Of("MusicDef"), "Id") is { } kit ? "music:" + kit : act.Has("SetMusic") ? "default" : null;

			case Kind.PinGroups:
				if (act.Has("SetPin"))
				{
					return "default";
				}
				return act.Str("name") is { } pinGroup ? "group:" + pinGroup : null;

			case Kind.Pins:
				return Prop(act.Of("PinDef"), "Id") is { } pin ? "pin:" + pin : null;

			case Kind.StickerSlots:
				if (act.Direct == "PromptStickerSearch")
				{
					return "search";
				}
				if (act.Has("OpenStickerPicker"))
				{
					return act.Int("index") is { } slot ? "slot:" + slot : "slot-all";
				}
				return act.Has("StickerRemoveMenu") ? "remove" : null;

			case Kind.StickerRemove:
				if (act.Direct == "RemoveAllStickers")
				{
					return "remove-all";
				}
				return act.Int("index") is { } removed ? "slot:" + removed : null;

			case Kind.StickerSources:
				return act.Str("value") is { } source ? "source:" + source : null;

			case Kind.StickerEvents:
				return act.Str("value") is { } group ? "group:" + group : null;

			case Kind.StickerTypes:
				return act.Str("value") is { } type ? "type:" + type : null;

			case Kind.StickerAudience:
				return act.Strings.Contains("Teams") ? "audience:teams" : act.Strings.Contains("Players") ? "audience:players" : index == 0 ? "audience:teams" : "audience:players";

			case Kind.Stickers:
				return Prop(act.Of("StickerDef"), "Id") is { } sticker ? "sticker:" + sticker : null;

			case Kind.StickerSearch:
				if (Prop(act.Of("StickerDef"), "Id") is { } found)
				{
					return "sticker:" + found;
				}
				return act.Has("StickerSourceMenu") ? "search-all" : null;
		}
		return null;
	}

	private static readonly ConditionalWeakTable<Type, Dictionary<string, PropertyInfo?>> Props = new();

	private static string? Prop(object? value, string name)
	{
		if (value == null)
		{
			return null;
		}
		Type type = value.GetType();
		Dictionary<string, PropertyInfo?> byName = Props.GetOrCreateValue(type);
		if (!byName.TryGetValue(name, out PropertyInfo? property))
		{
			property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
			byName[name] = property;
		}
		object? result = property?.GetValue(value);
		return result switch
		{
			null => null,
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => result.ToString()
		};
	}

	public static int StickerId(object item)
	{
		try
		{
			Delegate? select = SelectOf(item);
			if (select == null)
			{
				return 0;
			}
			object? sticker = Analyze(select).Of("StickerDef");
			return Prop(sticker, "Id") is { } id && int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : 0;
		}
		catch (Exception)
		{
			return 0;
		}
	}
}
