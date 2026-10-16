using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PMM_WeaponSkins;

internal sealed class ItemIndex
{
	private readonly Dictionary<long, byte> _skin = new();
	private readonly Dictionary<int, byte> _sticker = new();
	private readonly Dictionary<string, byte> _agent = new(StringComparer.Ordinal);
	private readonly Dictionary<int, byte> _music = new();
	private readonly Dictionary<int, byte> _pin = new();
	private readonly Dictionary<int, float> _price = new();

	public void Load(string directory, ILogger logger)
	{
		string path = Path.Combine(directory, "ws_items.json");
		if (!File.Exists(path))
		{
			logger.LogWarning("ws_items.json was not found next to PMM_WeaponSkins.dll. Cards have no rarity frames until it is there.");
			return;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			JsonElement root = document.RootElement;
			Fill(root, "skin", (key, value) =>
			{
				int colon = key.IndexOf(':');
				if (colon > 0 && int.TryParse(key.AsSpan(0, colon), out int def) && int.TryParse(key.AsSpan(colon + 1), out int paint))
				{
					_skin[SkinKey(def, paint)] = value;
				}
			});
			Fill(root, "sticker", (key, value) => { if (int.TryParse(key, out int id)) _sticker[id] = value; });
			Fill(root, "agent", (key, value) => _agent[key] = value);
			Fill(root, "music", (key, value) => { if (int.TryParse(key, out int id)) _music[id] = value; });
			Fill(root, "pin", (key, value) => { if (int.TryParse(key, out int id)) _pin[id] = value; });
			if (root.TryGetProperty("price", out JsonElement prices) && prices.ValueKind == JsonValueKind.Object)
			{
				foreach (JsonProperty property in prices.EnumerateObject())
				{
					if (int.TryParse(property.Name, out int id) && property.Value.TryGetSingle(out float price) && price > 0f)
					{
						_price[id] = price;
					}
				}
			}
			logger.LogInformation("ws_items.json: {Skins} skins, {Stickers} stickers ({Prices} with a price), {Agents} agents, {Music} music kits, {Pins} pins.",
				_skin.Count, _sticker.Count, _price.Count, _agent.Count, _music.Count, _pin.Count);
		}
		catch (Exception ex)
		{
			logger.LogWarning("ws_items.json could not be read: {Message}", ex.Message);
		}
	}

	private static void Fill(JsonElement root, string section, Action<string, byte> add)
	{
		if (!root.TryGetProperty(section, out JsonElement element) || element.ValueKind != JsonValueKind.Object)
		{
			return;
		}
		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (property.Value.TryGetByte(out byte value))
			{
				add(property.Name, value);
			}
		}
	}

	private static long SkinKey(int def, int paint) => ((long)def << 32) | (uint)paint;

	public bool IsEmpty => _skin.Count == 0 && _sticker.Count == 0;

	public bool HasSkin(int def, int paint) => _skin.ContainsKey(SkinKey(def, paint));

	public bool HasSticker(int id) => _sticker.ContainsKey(id);

	public bool HasAgent(string model) => _agent.ContainsKey(Art.AgentKey(model));

	public bool HasMusic(int id) => _music.ContainsKey(id);

	public bool HasPin(int id) => _pin.ContainsKey(id);

	public int Skin(int def, int paint) => _skin.TryGetValue(SkinKey(def, paint), out byte value) ? value : 0;

	public int Sticker(int id) => _sticker.TryGetValue(id, out byte value) ? value : 0;

	public int Agent(string? model) => model != null && _agent.TryGetValue(Art.AgentKey(model), out byte value) ? value : 0;

	public int Music(int id) => _music.TryGetValue(id, out byte value) ? value : 0;

	public int Pin(int id) => _pin.TryGetValue(id, out byte value) ? value : 0;

	public int BestSticker(IReadOnlyList<int> ids)
	{
		int best = 0;
		float bestPrice = 0f;
		int bestRarity = 0;
		for (int i = 0; i < ids.Count; i++)
		{
			int id = ids[i];
			if (!_sticker.TryGetValue(id, out byte rarity))
			{
				continue;
			}
			float price = _price.TryGetValue(id, out float known) ? known : 0f;
			bool better = best == 0
				|| price > bestPrice
				|| (price == bestPrice && (rarity > bestRarity || (rarity == bestRarity && id < best)));
			if (better)
			{
				best = id;
				bestPrice = price;
				bestRarity = rarity;
			}
		}
		return best;
	}
}

internal static class Art
{
	public const string Folder = "ws-folder";
	public const string KnifeBase = "bw-knife";

	public static string Skin(int def, int paint) => "sk-" + def + "-" + paint;

	public static string Base(int def) => "bw-" + def;

	public static string Sticker(int id) => "st-" + id;

	public static string Music(int id) => id > 0 ? "mk-" + id : "mk-none";

	public static string Pin(int id) => id > 0 ? "pn-" + id : "pn-none";

	public static string Agent(string model) => "ag-" + Safe(AgentKey(model));

	public static string DefaultAgent(bool ct) => ct ? "ag-def-ct" : "ag-def-t";

	public static string DefaultGloves(bool ct) => ct ? "gl-def-ct" : "gl-def-t";

	public static string AgentKey(string model)
	{
		int slash = model.LastIndexOf('/');
		string name = slash >= 0 ? model[(slash + 1)..] : model;
		return name.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase) ? name[..^5] : name;
	}

	private static string Safe(string name)
	{
		char[] chars = name.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			if (!char.IsAsciiLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-')
			{
				chars[i] = '_';
			}
		}
		return new string(chars);
	}
}
