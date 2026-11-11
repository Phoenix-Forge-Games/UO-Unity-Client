#region References
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

using Server.Accounting;
using Server.Commands;
using Server.Items;
using Server.Mobiles;
using Server.Multis;
#endregion

namespace Server.Custom
{
	/// <summary>
	/// Owner tool for moving a ServUO shard's players into the Unity game (its Docs/ServUOImport.md): writes every account, its characters (body, stats, skills, fame,
	/// karma, where they stand), everything they wear and carry and keep in the bank (with contents, magic properties,
	/// quality, material, spellbook contents and rune destinations), their pets (following, stabled, ridden) and their
	/// houses (size, friends, and everything locked down or secured in them) to one JSON file. Read-only: nothing in the
	/// world changes. Run against a copy of the live save. Usage: [ExportForUnity [path]
	/// Each item and pet also lists the classes its own class derives from, so a shard's custom classes can come over
	/// as the nearest standard one. Uses only stock ServUO types (and reflection for magic properties), so it drops into
	/// any ServUO's Scripts/Custom.
	/// </summary>
	public static class ExportForUnity
	{
		public static void Initialize()
		{
			CommandSystem.Register("ExportForUnity", AccessLevel.Owner, e =>
			{
				string path = e.Length > 0 ? e.GetString(0) : Path.Combine(Core.BaseDirectory, "Export", "servuo-export.json");

				try
				{
					e.Mobile.SendMessage(Run(path));
				}
				catch (Exception ex)
				{
					e.Mobile.SendMessage("Export failed: {0}", ex.Message);
					Console.WriteLine(ex);
				}
			});
		}

		public static string Run(string path)
		{
			var w = new Json();
			int accounts = 0, characters = 0, items = 0, pets = 0, houses = 0;

			w.Open('{');
			w.Prop("Source", "SeriousFun (ServUO)");
			w.Prop("Exported", DateTime.UtcNow.ToString("o"));
			w.Prop("CurrencyThreshold", AccountGold.CurrencyThreshold);
			w.Key("Accounts");
			w.Open('[');

			foreach (IAccount ia in Accounts.GetAccounts())
			{
				var a = ia as Account;

				if (a == null)
				{
					continue;
				}

				accounts++;
				w.Open('{');
				w.Prop("Username", a.Username);
				w.Prop("AccessLevel", a.AccessLevel.ToString());
				w.Prop("Created", a.Created.ToUniversalTime().ToString("o"));
				w.Prop("Banned", a.Banned);
				w.Prop("PasswordSha1", Private(a, "_SHA1Password"));
				w.Prop("PasswordSha512", Private(a, "_SHA512Password"));
				w.Prop("PasswordMd5", Private(a, "_MD5Password"));
				w.Prop("CurrencyPlat", a.TotalPlat);
				w.Prop("CurrencyGold", a.TotalGold);
				w.Key("Characters");
				w.Open('[');

				for (int i = 0; i < a.Length; i++)
				{
					var pm = a[i] as PlayerMobile;

					if (pm == null || pm.Deleted)
					{
						continue;
					}

					characters++;
					WriteCharacter(w, pm, ref items, ref pets, ref houses);
				}

				w.Close(']');
				w.Close('}');
			}

			w.Close(']');
			w.Close('}');

			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
			File.WriteAllText(path, w.ToString(), new UTF8Encoding(false));
			return string.Format("Exported {0} accounts, {1} characters, {2} items, {3} pets, {4} houses to {5}", accounts, characters, items, pets, houses, path);
		}

		private static string Private(object o, string field)
		{
			FieldInfo f = o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
			return f != null ? f.GetValue(o) as string : null;
		}

		private static void WriteCharacter(Json w, PlayerMobile pm, ref int items, ref int pets, ref int houses)
		{
			w.Open('{');
			w.Prop("Serial", pm.Serial.Value);
			w.Prop("Name", pm.RawName);
			w.Prop("Female", pm.Female);
			w.Prop("Race", pm.Race != null ? pm.Race.Name : "Human");
			w.Prop("Body", pm.Body.BodyID);
			w.Prop("Hue", pm.Hue);
			w.Prop("HairItemID", pm.HairItemID);
			w.Prop("HairHue", pm.HairHue);
			w.Prop("BeardItemID", pm.FacialHairItemID);
			w.Prop("BeardHue", pm.FacialHairHue);
			w.Prop("Str", pm.RawStr);
			w.Prop("Dex", pm.RawDex);
			w.Prop("Int", pm.RawInt);
			w.Prop("StrLock", pm.StrLock.ToString());
			w.Prop("DexLock", pm.DexLock.ToString());
			w.Prop("IntLock", pm.IntLock.ToString());
			w.Prop("Fame", pm.Fame);
			w.Prop("Karma", pm.Karma);
			w.Prop("Tithing", pm.TithingPoints);
			w.Prop("Alive", pm.Alive);
			w.Prop("Map", pm.Map != null && pm.Map != Map.Internal ? pm.Map.Name : (pm.LogoutMap != null ? pm.LogoutMap.Name : "Trammel"));
			Point3D at = pm.Map == Map.Internal ? pm.LogoutLocation : pm.Location;
			w.Prop("X", at.X);
			w.Prop("Y", at.Y);
			w.Prop("Z", at.Z);
			w.Prop("Direction", (int)(pm.Direction & Direction.Mask));
			w.Prop("Created", pm.CreationTime.ToUniversalTime().ToString("o"));

			w.Key("Skills");
			w.Open('[');

			for (int i = 0; i < pm.Skills.Length; i++)
			{
				Skill s = pm.Skills[i];

				if (s == null || (s.BaseFixedPoint == 0 && s.Lock == SkillLock.Up))
				{
					continue;
				}

				w.Open('{');
				w.Prop("Name", s.SkillName.ToString());
				w.Prop("Base", s.BaseFixedPoint / 10.0);
				w.Prop("Cap", s.CapFixedPoint / 10.0);
				w.Prop("Lock", s.Lock.ToString());
				w.Close('}');
			}

			w.Close(']');

			// Worn: everything on the body but the backpack, bank box, hair and beard (those are written above).
			w.Key("Equipped");
			w.Open('[');

			foreach (Item item in pm.Items)
			{
				if (item == pm.Backpack || item is BankBox || item.Layer == Layer.Hair || item.Layer == Layer.FacialHair || item.Layer == Layer.Mount)
				{
					continue;
				}

				WriteItem(w, item, ref items);
			}

			w.Close(']');

			w.Key("Backpack");
			WriteContents(w, pm.Backpack, ref items);
			w.Key("Bank");
			WriteContents(w, pm.FindBankNoCreate(), ref items);

			// Pets: those following (in the world or ridden) and those in a stable.
			var seen = new HashSet<Mobile>();
			w.Key("Pets");
			w.Open('[');

			foreach (Mobile m in World.Mobiles.Values)
			{
				var bc = m as BaseCreature;

				if (bc != null && !bc.Deleted && bc.Controlled && bc.ControlMaster == pm && !bc.Summoned && seen.Add(bc))
				{
					pets++;
					WritePet(w, bc, pm, false, ref items);
				}
			}

			foreach (Mobile m in pm.Stabled)
			{
				var bc = m as BaseCreature;

				if (bc != null && !bc.Deleted && seen.Add(bc))
				{
					pets++;
					WritePet(w, bc, pm, true, ref items);
				}
			}

			w.Close(']');

			w.Key("Houses");
			w.Open('[');

			foreach (BaseHouse house in BaseHouse.AllHouses)
			{
				if (house == null || house.Deleted || house.Owner != pm)
				{
					continue;
				}

				houses++;
				WriteHouse(w, house, ref items);
			}

			w.Close(']');
			w.Close('}');
		}

		private static void WritePet(Json w, BaseCreature bc, PlayerMobile owner, bool stabled, ref int items)
		{
			w.Open('{');
			w.Prop("Serial", bc.Serial.Value);
			w.Prop("Type", bc.GetType().Name);
			BaseTypes(w, bc.GetType());
			w.Prop("Name", bc.RawName);
			w.Prop("Hue", bc.Hue);
			w.Prop("Body", bc.Body.BodyID);
			w.Prop("Loyalty", bc.Loyalty);
			w.Prop("Bonded", bc.IsBonded);
			w.Prop("Dead", bc.IsDeadPet);
			w.Prop("Stabled", stabled || bc.IsStabled);
			w.Prop("Ridden", owner.Mount != null && owner.Mount == bc as IMount);
			w.Prop("ControlSlots", bc.ControlSlots);
			w.Prop("Hits", bc.Hits);
			w.Prop("HitsMax", bc.HitsMax);
			w.Prop("Str", bc.RawStr);
			w.Prop("Dex", bc.RawDex);
			w.Prop("Int", bc.RawInt);
			w.Key("Owners");
			w.Open('[');

			if (bc.Owners != null)
			{
				foreach (Mobile o in bc.Owners)
				{
					if (o != null)
					{
						w.Value(o.Serial.Value);
					}
				}
			}

			w.Close(']');
			w.Key("Skills");
			w.Open('[');

			for (int i = 0; i < bc.Skills.Length; i++)
			{
				Skill s = bc.Skills[i];

				if (s != null && s.BaseFixedPoint > 0)
				{
					w.Open('{');
					w.Prop("Name", s.SkillName.ToString());
					w.Prop("Base", s.BaseFixedPoint / 10.0);
					w.Close('}');
				}
			}

			w.Close(']');
			w.Key("Pack");
			WriteContents(w, bc.Backpack, ref items);
			w.Close('}');
		}

		private static void WriteHouse(Json w, BaseHouse house, ref int items)
		{
			w.Open('{');
			w.Prop("Serial", house.Serial.Value);
			w.Prop("Type", house.GetType().Name);
			w.Prop("Name", house.Sign != null ? house.Sign.Name : null);
			w.Prop("Map", house.Map != null ? house.Map.Name : null);
			w.Prop("X", house.X);
			w.Prop("Y", house.Y);
			w.Prop("Z", house.Z);
			MultiComponentList mcl = house.Components;
			w.Prop("Width", mcl != null ? mcl.Width : 0);
			w.Prop("Height", mcl != null ? mcl.Height : 0);
			w.Prop("Public", house.Public);
			w.Key("CoOwners");
			Names(w, house.CoOwners);
			w.Key("Friends");
			Names(w, house.Friends);

			// Everything locked down or secured (with what's inside); things only lying on the floor aren't the owner's
			// for sure, so they stay behind.
			var roots = new HashSet<Item>();

			if (house.LockDowns != null)
			{
				foreach (Item i in house.LockDowns.Keys)
				{
					roots.Add(i);
				}
			}

			if (house.Secures != null)
			{
				foreach (SecureInfo s in house.Secures)
				{
					if (s != null && s.Item != null)
					{
						roots.Add(s.Item);
					}
				}
			}

			w.Key("Items");
			w.Open('[');

			foreach (Item i in roots)
			{
				if (i != null && !i.Deleted && (i.Parent == null || !roots.Contains(i.Parent as Item)))
				{
					WriteItem(w, i, ref items);
				}
			}

			w.Close(']');
			w.Close('}');
		}

		private static void Names(Json w, List<Mobile> list)
		{
			w.Open('[');

			if (list != null)
			{
				foreach (Mobile m in list)
				{
					if (m != null)
					{
						w.Value(m.RawName);
					}
				}
			}

			w.Close(']');
		}

		private static void WriteContents(Json w, Container c, ref int items)
		{
			w.Open('[');

			if (c != null)
			{
				foreach (Item i in c.Items)
				{
					WriteItem(w, i, ref items);
				}
			}

			w.Close(']');
		}

		private static void WriteItem(Json w, Item item, ref int items)
		{
			if (item == null || item.Deleted)
			{
				return;
			}

			items++;
			w.Open('{');
			w.Prop("Serial", item.Serial.Value);
			w.Prop("Type", item.GetType().Name);
			BaseTypes(w, item.GetType());
			w.Prop("ItemID", item.ItemID);
			w.Prop("Name", item.Name);
			w.Prop("Amount", item.Amount);
			w.Prop("Hue", item.Hue);
			w.Prop("Layer", item.Layer.ToString());

			object quality = Get(item, "Quality");

			if (quality != null)
			{
				w.Prop("Quality", quality.ToString());
			}

			object resource = Get(item, "Resource");

			if (resource != null)
			{
				w.Prop("Resource", resource.ToString());
			}

			var book = item as Spellbook;

			if (book != null)
			{
				w.Prop("SpellbookType", book.SpellbookType.ToString());
				w.Prop("Content", book.Content.ToString(CultureInfo.InvariantCulture));
			}

			var rune = item as RecallRune;

			if (rune != null)
			{
				w.Prop("Marked", rune.Marked);
				w.Prop("TargetX", rune.Target.X);
				w.Prop("TargetY", rune.Target.Y);
				w.Prop("TargetZ", rune.Target.Z);
				w.Prop("TargetMap", rune.TargetMap != null ? rune.TargetMap.Name : null);
				w.Prop("Description", rune.Description);
			}

			var runebook = item as Runebook;

			if (runebook != null)
			{
				w.Key("Runes");
				w.Open('[');

				foreach (RunebookEntry e in runebook.Entries)
				{
					w.Open('{');
					w.Prop("X", e.Location.X);
					w.Prop("Y", e.Location.Y);
					w.Prop("Z", e.Location.Z);
					w.Prop("Map", e.Map != null ? e.Map.Name : null);
					w.Prop("Description", e.Description);
					w.Close('}');
				}

				w.Close(']');
			}

			WriteProps(w, item);

			var container = item as Container;

			if (container != null && container.Items.Count > 0)
			{
				w.Key("Contents");
				WriteContents(w, container, ref items);
			}

			w.Close('}');
		}

		/// <summary>The classes a class derives from, nearest first, up to (not including) Item or Mobile.</summary>
		private static void BaseTypes(Json w, Type type)
		{
			w.Key("BaseTypes");
			w.Open('[');

			for (Type b = type.BaseType; b != null && b != typeof(Item) && b != typeof(Mobile) && b != typeof(object); b = b.BaseType)
			{
				w.Value(b.Name);
			}

			w.Close(']');
		}

		private static object Get(object o, string name)
		{
			PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
			return p != null && p.CanRead && p.GetIndexParameters().Length == 0 ? p.GetValue(o, null) : null;
		}

		/// <summary>
		/// Magic properties as "Group.Name": value (Group: the ServUO enum, as the Unity game's Magic.json names them).
		/// Element attributes say which property they came from (resists or weapon damage split).
		/// </summary>
		private static void WriteProps(Json w, Item item)
		{
			var props = new List<KeyValuePair<string, double>>();

			foreach (PropertyInfo p in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				if (p.GetIndexParameters().Length != 0 || !p.CanRead)
				{
					continue;
				}

				Type t = p.PropertyType;

				if (typeof(BaseAttributes).IsAssignableFrom(t) && t != typeof(AosSkillBonuses))
				{
					string enumName = t.Name.EndsWith("s") ? t.Name.Substring(0, t.Name.Length - 1) : t.Name;
					Type enumType = t.Assembly.GetType("Server." + enumName) ?? t.Assembly.GetType("Server.Items." + enumName);
					object attrs;

					try
					{
						attrs = p.GetValue(item, null);
					}
					catch
					{
						continue;
					}

					if (attrs == null || enumType == null || !enumType.IsEnum)
					{
						continue;
					}

					PropertyInfo indexer = t.GetProperty("Item", new[] { enumType });

					if (indexer == null)
					{
						continue;
					}

					string group = enumName;

					if (enumName == "AosElementAttribute")
					{
						group = p.Name == "Resistances" ? "AosElementAttribute" : "ElementDamage." + p.Name;
					}

					foreach (object v in Enum.GetValues(enumType))
					{
						int value;

						try
						{
							value = Convert.ToInt32(indexer.GetValue(attrs, new[] { v }));
						}
						catch
						{
							continue;
						}

						if (value != 0)
						{
							props.Add(new KeyValuePair<string, double>(group + "." + v, value));
						}
					}
				}
				else if (t == typeof(AosSkillBonuses))
				{
					var bonuses = p.GetValue(item, null) as AosSkillBonuses;

					for (int i = 0; bonuses != null && i < 5; i++)
					{
						SkillName skill;
						double bonus;

						if (bonuses.GetValues(i, out skill, out bonus) && bonus != 0)
						{
							props.Add(new KeyValuePair<string, double>("SkillName." + skill, bonus));
						}
					}
				}
				else if ((t == typeof(SlayerName) || t == typeof(TalismanSlayerName)) && p.Name.StartsWith("Slayer"))
				{
					object v = p.GetValue(item, null);

					if (v != null && Convert.ToInt32(v) != 0)
					{
						props.Add(new KeyValuePair<string, double>(t.Name + "." + v, 1));
					}
				}
			}

			// Armour's own resist bonuses (magic armour keeps them outside the attribute sets).
			foreach (string r in new[] { "Physical", "Fire", "Cold", "Poison", "Energy" })
			{
				var armor = item as BaseArmor;

				if (armor != null)
				{
					object v = Get(armor, r + "Bonus");

					if (v is int && (int)v != 0)
					{
						props.Add(new KeyValuePair<string, double>("ArmorBonus." + r, (int)v));
					}
				}
			}

			if (props.Count == 0)
			{
				return;
			}

			w.Key("Props");
			w.Open('{');

			foreach (KeyValuePair<string, double> kv in props)
			{
				w.Prop(kv.Key, kv.Value);
			}

			w.Close('}');
		}

		/// <summary>A small JSON writer (no dependencies).</summary>
		private sealed class Json
		{
			private readonly StringBuilder _sb = new StringBuilder();
			private readonly Stack<bool> _first = new Stack<bool>();
			private bool _afterKey;

			private void Comma()
			{
				if (_afterKey)
				{
					_afterKey = false;
					return;
				}

				if (_first.Count > 0)
				{
					if (!_first.Peek())
					{
						_sb.Append(',');
					}

					_first.Pop();
					_first.Push(false);
				}
			}

			public void Open(char c)
			{
				Comma();
				_sb.Append(c);
				_first.Push(true);
			}

			public void Close(char c)
			{
				_first.Pop();
				_sb.Append(c);
			}

			public void Key(string k)
			{
				Comma();
				Str(k);
				_sb.Append(':');
				_afterKey = true;
			}

			public void Prop(string k, string v)
			{
				Key(k);
				Value(v);
			}

			public void Prop(string k, int v)
			{
				Key(k);
				Value(v);
			}

			public void Prop(string k, double v)
			{
				Key(k);
				Comma();
				_sb.Append(v.ToString("R", CultureInfo.InvariantCulture));
			}

			public void Prop(string k, bool v)
			{
				Key(k);
				Comma();
				_sb.Append(v ? "true" : "false");
			}

			public void Value(int v)
			{
				Comma();
				_sb.Append(v.ToString(CultureInfo.InvariantCulture));
			}

			public void Value(string v)
			{
				Comma();

				if (v == null)
				{
					_sb.Append("null");
				}
				else
				{
					Str(v);
				}
			}

			private void Str(string s)
			{
				_sb.Append('"');

				foreach (char ch in s)
				{
					switch (ch)
					{
						case '"': _sb.Append("\\\""); break;
						case '\\': _sb.Append("\\\\"); break;
						case '\n': _sb.Append("\\n"); break;
						case '\r': _sb.Append("\\r"); break;
						case '\t': _sb.Append("\\t"); break;
						default:
							if (ch < 0x20)
							{
								_sb.Append("\\u").Append(((int)ch).ToString("x4"));
							}
							else
							{
								_sb.Append(ch);
							}

							break;
					}
				}

				_sb.Append('"');
			}

			public override string ToString()
			{
				return _sb.ToString();
			}
		}
	}
}
