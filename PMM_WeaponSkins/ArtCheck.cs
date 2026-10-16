namespace PMM_WeaponSkins;

internal sealed class CatalogContents
{
	public readonly List<(int Def, int Paint)> Skins = new();
	public readonly List<int> Stickers = new();
	public readonly List<string> Agents = new();
	public readonly List<int> Music = new();
	public readonly List<int> Pins = new();
}

internal sealed class ArtGaps
{
	public int Skins;
	public int Stickers;
	public int Agents;
	public int Music;
	public int Pins;
	public string Example = "";

	public int Total => Skins + Stickers + Agents + Music + Pins;
}

internal static class ArtCheck
{
	public static ArtGaps Find(ItemIndex items, CatalogContents catalog)
	{
		ArtGaps gaps = new();
		foreach ((int def, int paint) in catalog.Skins)
		{
			if (!items.HasSkin(def, paint))
			{
				gaps.Skins++;
				gaps.Example = gaps.Example.Length == 0 ? "skin " + def + ":" + paint : gaps.Example;
			}
		}
		foreach (int id in catalog.Stickers)
		{
			if (!items.HasSticker(id))
			{
				gaps.Stickers++;
				gaps.Example = gaps.Example.Length == 0 ? "sticker " + id : gaps.Example;
			}
		}
		foreach (string model in catalog.Agents)
		{
			if (!items.HasAgent(model))
			{
				gaps.Agents++;
				gaps.Example = gaps.Example.Length == 0 ? "agent " + Art.AgentKey(model) : gaps.Example;
			}
		}
		foreach (int id in catalog.Music)
		{
			if (!items.HasMusic(id))
			{
				gaps.Music++;
				gaps.Example = gaps.Example.Length == 0 ? "music kit " + id : gaps.Example;
			}
		}
		foreach (int id in catalog.Pins)
		{
			if (!items.HasPin(id))
			{
				gaps.Pins++;
				gaps.Example = gaps.Example.Length == 0 ? "pin " + id : gaps.Example;
			}
		}
		return gaps;
	}
}
