using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace EkiEVS
{
	class Program
	{
		static void Main(string[] args)
		{
			if (args.Length < 1)
			{
				Console.WriteLine("Please specify input xml file as argument");
				return;
			}
			
			Run(args[0]);
		}

		private static void Run(string xmlPath)
		{
			Dictionary<string, List<Sortable<Schema.A>>> wordsDictionary = null;

			try
			{
				Console.WriteLine($"Reading input xml file \"{xmlPath}\"");
				wordsDictionary = GetWordsDictionary(xmlPath);
			}
			catch (Exception e)
			{
				Console.WriteLine($"Failed to read input xml: {e.Message}");
				return;
			}
			
			try
			{
				string path = Path.ChangeExtension(xmlPath, "dsl");
				
				Console.WriteLine($"Writing output dsl file \"{path}\"");
				WriteWordsDictionaryToDsl(path, wordsDictionary);
			}
			catch (Exception e)
			{
				Console.WriteLine($"Failed to write outpot dsl: {e.Message}");
				return;
			}

			try
			{
				string path = Path.Combine(Path.GetDirectoryName(xmlPath), Path.GetFileNameWithoutExtension(xmlPath) + "_abrv.dsl");

				Console.WriteLine($"Writing abbreviation dsl file \"{path}\"");

				using (FileStream file = new FileStream(path, FileMode.Create))
				{
					file.Write(Resources.abbreviations, 0, Resources.abbreviations.Length);
					file.Close();
				}
			}
			catch (Exception e)
			{
				Console.WriteLine($"Failed to write abbreviation dsl file: {e.Message}");
				return;
			}

			try
			{
				string path = Path.ChangeExtension(xmlPath, "ann");

				Console.WriteLine($"Writing annotation file \"{path}\"");

				using (FileStream file = new FileStream(path, FileMode.Create))
				{
					file.Write(Resources.annotation, 0, Resources.annotation.Length);
					file.Close();
				}
			}
			catch (Exception e)
			{
				Console.WriteLine($"Failed to write annotation file: {e.Message}");
				return;
			}

			Console.WriteLine("Completed!");
		}
		
		private static Dictionary<string, List<Sortable<Schema.A>>> GetWordsDictionary(string xmlPath)
		{
			Dictionary<string, List<Sortable<Schema.A>>> wordsMap = new Dictionary<string, List<Sortable<Schema.A>>>();

			using (FileStream inputFileStream = new FileStream(xmlPath, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (XmlReader reader = CreateXmlReader(inputFileStream))
			{
				XmlSerializer serializer = new XmlSerializer(typeof(Schema.A));

				Schema.A word = null;

				while ((word = GetNextWord(reader, serializer)) != null)
				{
					if (String.IsNullOrEmpty(word.P.mg.m.O))
					{
						Console.WriteLine($"Empty sortvalue for {GetCardHeadword(word)}");
						continue;
					}

					string headword = GetCardHeadword(word);

					if (String.IsNullOrEmpty(headword))
					{
						Console.WriteLine("Empty headword!");
						continue;
					}

					headword = ClearHeadword(headword);
					
					if (String.IsNullOrEmpty(headword))
					{
						Console.WriteLine("Empty headword after cleaning!");
						continue;
					}

					List<Sortable<Schema.A>> words = null;

					if (!wordsMap.TryGetValue(headword, out words))
					{
						words = new List<Sortable<Schema.A>>();
						wordsMap.Add(headword, words);
					}

					if (word.P.mg.m.i != null)
					{
						int homonymNumber = 0;

						if (!Int32.TryParse(word.P.mg.m.i, out homonymNumber))
						{
							Console.WriteLine($"Failed to parse homonym number for headword \"{headword}\" for word \"{word.P.mg.m.Value})\"");
							continue;
						}

						words.Add(new Sortable<Schema.A> { Object = word, SortOrder = homonymNumber });
					}
					else
					{
						words.Add(new Sortable<Schema.A> { Object = word, SortOrder = 0 });
					}
				}
			}

			return wordsMap;
		}

		private static void WriteWordsDictionaryToDsl(string dslPath, Dictionary<string, List<Sortable<Schema.A>>> wordsDictionary)
		{
			using (StreamWriter writer = new StreamWriter(dslPath, false, Encoding.Unicode))
			{
				WriteDslHeader(writer);

				List<Schema.A> cards = new List<Schema.A>();

				foreach (KeyValuePair<string, List<Sortable<Schema.A>>> kv in wordsDictionary)
				{
					kv.Value.Sort((x, y) =>
					{
						if (x.SortOrder < y.SortOrder)
							return -1;

						if (x.SortOrder == y.SortOrder)
							return 0;

						return 1;
					});

					foreach (Sortable<Schema.A> sortableWord in kv.Value)
						cards.Add(sortableWord.Object);

					WriteWord(writer, kv.Key, cards);
					writer.Flush();
					cards.Clear();
				}

				writer.Close();
			}
		}
		
		private static void WriteWord(StreamWriter writer, string headword, List<Schema.A> cards)
		{
			if (cards.Count == 0)
				return;
			
			if (cards.Count > 1)
			{
				writer.WriteLine(headword);

				for (int i = 0; i < cards.Count; ++i)
				{
					Schema.A card = cards[i];

					// Writing word number

					writer.Write("\t[b]");
					writer.Write(i + 1);
					writer.Write(".[/b]");

					WritePartOfSpeech(writer, card, " ");

					string cardHeadword = GetCardHeadword(card);

					if (!String.IsNullOrEmpty(cardHeadword))
					{
						writer.Write(' ');
						
						WriteEscaped(writer, FixHeadword(cardHeadword));
					}
					
					// Need new line even if WritePartOfSpeech haven't write anything
					writer.WriteLine();

					WriteCard(writer, card);
				}
			}
			else
			{
				Schema.A card = cards[0];

				string cardHeadword = GetCardHeadword(card);

				if (String.IsNullOrEmpty(cardHeadword))
					Console.WriteLine($"Can't get headword for card {headword}");
				else
					writer.WriteLine(EscapeCardHeadword(FixHeadword(cardHeadword)));

				WritePartOfSpeech(writer, card, "\t", writer.NewLine);
				WriteCard(writer, card);
			}

			writer.WriteLine();
		}
		
		/// <summary>
		/// Fixes headword
		/// </summary>
		/// <param name="headword">Word to fix</param>
		/// <returns>Fixed word</returns>
		private static string FixHeadword(string headword)
		{
			int underscorePos = headword.IndexOf('_');

			if (underscorePos > 0)
				headword = headword.Substring(0, underscorePos);

			headword = headword.Replace(" %v ", " ~ ");
			headword = headword.Replace("|", "");

			return headword;
		}

		private static string ClearHeadword(string headword)
		{
			headword = headword.Replace(" %v ", " ~ ");

			StringBuilder cleaned = new StringBuilder(headword.Length);

			bool stop = false;
			bool waitClose = false;

			foreach (char ch in headword)
			{
				switch (ch)
				{
					case '[':
						if (!waitClose)
							waitClose = true;
						break;
					case '|':
					case '+':
						break;
					case ']':
						if (waitClose)
							waitClose = false;
						break;
					case '_':
						if (waitClose)
							waitClose = false;

						stop = true;

						break;
					default:
						if (waitClose)
							break;

						cleaned.Append(ch);
						break;
				}

				if (stop)
					break;
			}

			return cleaned.ToString();
		}

		private static void WriteSubCardHeadword(StreamWriter writer, string headword)
		{
			headword = headword.Replace(" %v ", " ~ ");
			
			foreach (char ch in headword)
			{
				switch (ch)
				{
					case '[':
					case ']':
						writer.Write("{\\");
						writer.Write(ch);
						writer.Write('}');
						break;
					case '~':
						writer.Write('\\');
						writer.Write(ch);
						break;
					case '|':
					case '+':
					case '(':
					case ')':
						writer.Write('{');
						writer.Write(ch);
						writer.Write('}');
						break;
					default:
						writer.Write(ch);
						break;
				}
			}
		}

		/// <summary>
		/// Escapes headword so it cold be used as card headword
		/// </summary>
		/// <param name="headword">Word to escape</param>
		/// <returns>Escaped word</returns>
		private static string EscapeCardHeadword(string headword)
		{
			StringBuilder escaped = new StringBuilder(headword.Length);

			bool stop = false;
			bool waitClose = false;

			foreach (char ch in headword)
			{
				switch (ch)
				{
					case '[':
						if (!waitClose)
						{
							escaped.Append('{');
							waitClose = true;
						}

						escaped.Append('\\');
						escaped.Append(ch);

						break;
					case '|':
						break;
					case '+':
						if (waitClose)
						{
							escaped.Append(ch);
						}
						else
						{
							escaped.Append('{');
							escaped.Append(ch);
							escaped.Append('}');
						}

						break;
					case ']':
						escaped.Append('\\');
						escaped.Append(ch);

						if (waitClose)
						{
							escaped.Append('}');
							waitClose = false;
						}

						break;
					case '_':
						if (waitClose)
						{
							escaped.Append('}');
							waitClose = false;
						}

						stop = true;

						break;
					default:
						escaped.Append(ch);
						break;
				}

				if (stop)
					break;
			}

			return escaped.ToString();
		}

		private static bool WritePartOfSpeech(StreamWriter writer, Schema.A card, string prepend = null, string append = null)
		{
			if (card.P == null || card.P.grg == null)
				return false;

			const string comma = ", ";
			bool added = false;
			bool appendComma = false;
			bool addPrepend = prepend != null;
			bool addAppend = false;
			
			foreach (Schema.grg grg in card.P.grg)
			{
				if (String.IsNullOrEmpty(grg.sly))
					continue;

				if (addPrepend)
				{
					if (prepend != null)
						writer.Write(prepend);

					addPrepend = false;
					addAppend = true;
				}

				// ex: Adv_&_Postp
				if (grg.sly.Contains('&'))
				{
					string[] substrings = grg.sly.Split('&');

					foreach (string str in substrings)
					{
						WriteLabel(writer, str.Trim('_'), appendComma, comma);
						appendComma = true;
					}
				}
				else
				{
					WriteLabel(writer, grg.sly, appendComma, comma);
					appendComma = true;
				}

				added = true;
			}

			if (addAppend)
			{
				if (append != null)
					writer.Write(append);
			}

			return added;
		}

		private static string GetCardHeadword(Schema.A card)
		{
			if (card.P == null || card.P.mg == null || card.P.mg.m == null || String.IsNullOrEmpty(card.P.mg.m.Value))
				return null;

			return card.P.mg.m.Value;
		}

		private static void WriteCard(StreamWriter writer, Schema.A card)
		{
			// Adding forms

			if (card.P.grg != null)
			{
				foreach (Schema.grg grg in card.P.grg)
				{
					if (String.IsNullOrEmpty(grg.mv))
						continue;
					
					writer.Write('\t');
					WriteForms(writer, grg.mv);
					writer.WriteLine();
				}
			}

			// Adding translations

			for (int i = 0; i < card.S.Length; ++i)
			{
				Schema.tp tp = card.S[i];

				// Adding link to another translation

				if (tp.tvt != null)
				{
					bool writeOpenTag = true;
					bool writeCloseTag = false;
					bool appendComma = false;

					foreach (Schema.tvt tvt in tp.tvt)
					{
						if (String.IsNullOrEmpty(tvt.Value))
							continue;

						if (writeOpenTag)
						{
							writer.Write("\t[m1]");
							writeCloseTag = true;
						}

						if (appendComma)
							writer.Write(", ");
						else
							appendComma = true;

						writer.Write("<<");
						writer.Write(ClearHeadword(tvt.Value));
						writer.Write(">>");
					}

					if (writeCloseTag)
						writer.WriteLine("[/m]");
				}

				// Adding translation

				if (tp.tg != null)
				{
					bool appendComma = false;
					bool writeOpenTag = true;
					bool writeCloseTag = false;
					
					foreach (Schema.tg tg in tp.tg)
					{
						if (tg.xp == null || tg.xp.xg == null)
							continue;

						
						foreach (Schema.xg xg in tg.xp.xg)
						{
							foreach (string text in xg.x.Text)
							{
								if (String.IsNullOrEmpty(text))
									continue;

								if (writeOpenTag)
								{
									// Adding translation number

									writer.Write("\t[m1]");
									writer.Write(i + 1);
									writer.Write(") ");

									// Adding definitions
									
									if (tg.dg != null)
									{
										if (WriteDefinition(writer, tg.dg))
											writer.Write(" ");
									}


									// Adding labels

									if (tg.dg != null)
									{
										if (WriteDefinitionGroup(writer, tg.dg))
											writer.Write(' ');
									}

									writer.Write("[trn]");

									writeOpenTag = false;
									writeCloseTag = true;
								}

								if (appendComma)
									writer.Write(", ");
								else
									appendComma = true;

								WriteTranslation(writer, text.Trim());
								
								if (!String.IsNullOrEmpty(xg.aspvst))
								{
									writer.Write('/');
									WriteTranslation(writer, xg.aspvst);
								}

								if (xg.vstiil != null)
								{
									writer.Write(" (");

									appendComma = false;

									foreach (Schema.vstiil vstiil in xg.vstiil)
									{
										WriteLabel(writer, vstiil.Value.ToString(), appendComma, ", ");
										appendComma = true;
									}

									writer.Write(')');
								}
							}
						}
						
						
					}

					if (writeCloseTag)
					{
						writer.WriteLine("[/trn][/m]");
					}
				}

				// Adding examples

				if (tp.np != null)
				{
					bool writeExamleTagOpen = true;
					bool writeExamleTagClose = false;

					foreach (Schema.ng ng in tp.np)
					{
						// Write estonian

						if (ng.n != null)
						{
							bool writeLangTagOpen = true;
							bool writeLangTagClose = false;
							bool appendComma = false;

							foreach (Schema.n n in ng.n)
							{
								foreach (string text in n.Text)
								{
									if (String.IsNullOrEmpty(text))
										continue;

									if (writeExamleTagOpen)
									{
										writer.WriteLine("\t[*][ex][m2]");
										writeExamleTagOpen = false;
										writeExamleTagClose = true;
									}
									
									if (appendComma)
										writer.Write(", ");
									else
										appendComma = true;

									if (writeLangTagOpen)
									{
										writer.Write("\t\t[lang name=\"Estonian\"]");
										writeLangTagOpen = false;
										writeLangTagClose = true;
									}

									WriteExample(writer, text);
								}
							}

							if (writeLangTagClose)
								writer.Write("[/lang]");
						}

						// Write translation

						if (ng.qnp != null)
						{
							bool addSeparator = true;
							bool appendComma = false;

							foreach (Schema.qnp qnp in ng.qnp)
							{
								foreach (Schema.qng qng in qnp.qng)
								{
									string fullText = "";

									foreach (string text in qng.qn.Text)
									{
										if (String.IsNullOrEmpty(text))
											continue;

										fullText += text;
									}

									if (addSeparator)
									{
										writer.Write(" — ");
										addSeparator = false;
									}

									if (appendComma)
										writer.Write(", ");
									else
										appendComma = true;

									WriteTranslation(writer, fullText.Trim());
								}
							}
						}

						writer.WriteLine();
					}

					if (writeExamleTagClose)
						writer.WriteLine("\t[/m][/ex][/*]");
				}
			}

			// Adding phraselogical expressions

			if (card.F != null)
			{
				bool writeOpenTag = true;
				bool writeCloseTag = false;

				foreach (Schema.fg fg in card.F)
				{
					if (fg.f == null)
						continue;

					bool writeStartSubcard = true;
					bool writeCloseSubcard = false;
					bool appendComma = false;
					bool headerAdded = false;

					// Writing subcard headword

					foreach (Schema.f f in fg.f)
					{
						if (f.Text == null)
							continue;
						
						string headword = "";

						foreach (string text in f.Text)
						{
							if (String.IsNullOrEmpty(text))
								continue;

							headword += text;
						}
						
						if (writeOpenTag)
						{
							// Square character
							writer.WriteLine("\t[*]\u25a0");
							writeOpenTag = false;
							writeCloseTag = true;
						}

						if (writeStartSubcard)
						{
							writer.Write("\t@");
							writeStartSubcard = false;
							writeCloseSubcard = true;
						}

						if (appendComma)
							writer.Write(", ");
						else
							appendComma = true;
						
						WriteSubCardHeadword(writer, headword.Trim());
						
						headerAdded = true;
					}

					if (!headerAdded)
						break;

					writer.WriteLine();

					// Writing subcard translation

					if (fg.fqnp == null)
						break;

					appendComma = false;

					writer.Write("\t[m1]");

					foreach (Schema.fqnp fqnp in fg.fqnp)
					{
						if (fqnp.fqng == null)
							continue;

						foreach (Schema.fqng fqng in fqnp.fqng)
						{
							if (fqng.qf == null || fqng.qf.Text == null)
								continue;
							
							foreach (string text in fqng.qf.Text)
							{
								if (appendComma)
									writer.Write(", ");
								else
									appendComma = true;

								WriteTranslation(writer, text.Trim());
							}

							if (fqng.s != null)
							{
								writer.Write(" (");

								bool appendDelimiter = false;

								foreach (Schema.s s in fqng.s)
								{
									WriteLabel(writer, s.Value.ToString(), appendDelimiter);
									appendDelimiter = true;
								}

								writer.Write(")");
							}
						}
					}

					writer.WriteLine("[/m]");

					if (writeCloseSubcard)
						writer.WriteLine("\t@");
				}

				if (writeCloseTag)
				{
					writer.WriteLine("\t[/*]");
				}
			}
		}

		private static bool WriteDefinition(StreamWriter writer, Schema.dg[] definitions)
		{
			bool added = false;
			bool appendComma = false;
			bool writeOpenTag = true;
			bool writeCloseTag = false;

			foreach (Schema.dg dg in definitions)
			{
				if (dg.d == null)
					continue;

				foreach (Schema.d d in dg.d)
				{
					if (String.IsNullOrEmpty(d.Value))
						continue;

					if (appendComma)
						writer.Write("; ");
					else
						appendComma = true;

					if (writeOpenTag)
					{
						writer.Write("[com]([i]");
						writeOpenTag = false;
						writeCloseTag = true;
					}

					string value = d.Value.Replace(" %v ", " ~ ");

					WriteEscaped(writer, value);
					
					added = true;
				}
			}

			if (writeCloseTag)
				writer.Write("[/i])[/com]");

			return added;
		}

		/// <summary>
		/// Writes tag [p]label[/p]
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="label">Label to write</param>
		/// <param name="appendDelimiter">Append delimiter</param>
		/// <param name="delimiter">delimiter</param>
		private static void WriteLabel(StreamWriter writer, string label, bool appendDelimiter, string delimiter = ", ")
		{
			if (appendDelimiter)
				writer.Write(delimiter);

			writer.Write("[p]");
			writer.Write(label);
			writer.Write("[/p]");
		}

		private static bool WriteDefinitionGroup(StreamWriter writer, Schema.dg[] definitionGroups)
		{
			const string comma = "; ";
			bool added = false;
			bool appendComma = false;
			
			foreach (Schema.dg dg in definitionGroups)
			{
				if (dg.v != null)
				{
					foreach (Schema.v v in dg.v)
					{
						WriteLabel(writer, v.Value.ToString(), appendComma, comma);
						appendComma = true;
						added = true;
					}
				}

				if (dg.s != null)
				{
					foreach (Schema.s s in dg.s)
					{
						WriteLabel(writer, s.Value.ToString(), appendComma, comma);
						appendComma = true;
						added = true;
					}
				}
			}

			return added;
		}

		private static Schema.A GetNextWord(XmlReader reader, XmlSerializer serializer)
		{
			if (reader.EOF)
				return null;

			return (Schema.A)serializer.Deserialize(reader);
		}

		private static XmlReader CreateXmlReader(Stream stream)
		{
			XmlNamespaceManager namespaceManager = new XmlNamespaceManager(new NameTable());
			namespaceManager.AddNamespace("v", "http://www.eki.ee/dict/evs");

			XmlParserContext context = new XmlParserContext(null, namespaceManager, null, XmlSpace.None);

			XmlReaderSettings readerSettings = new XmlReaderSettings
			{
				ConformanceLevel = ConformanceLevel.Fragment
			};

			return XmlReader.Create(stream, readerSettings, context);
		}

		private static void WriteDslHeader(StreamWriter writer)
		{
			writer.WriteLine("#NAME \"Эстонско-Русский словарь (Et-Ru)\"");
			writer.WriteLine("#INDEX_LANGUAGE \"Estonian\"");
			writer.WriteLine("#CONTENTS_LANGUAGE \"Russian\"");
			writer.WriteLine();
			writer.Flush();
		}

		private static void WriteForms(StreamWriter writer, string forms)
		{
			forms = forms.Replace("_&_", " & ");

			WriteEscaped(writer, forms);
		}

		private static void WriteExample(StreamWriter writer, string example)
		{
			for (int i = 0; i < example.Length; ++i)
			{
				char ch = example[i];

				switch (ch)
				{
					case '%':
						// replace "%v"
						if (!WriteTilda(writer, example, ref i))
							writer.Write(ch);

						break;
					default:
						WriteEscaped(writer, ch);
						break;
				}
			}
		}

		private static void WriteTranslation(StreamWriter writer, string translation)
		{
			for (int i = 0; i < translation.Length; ++i)
			{
				char ch = translation[i];

				switch (ch)
				{
					// Accent
					case '"':
						++i;

						if (i >= translation.Length)
							break;

						writer.Write("[']");
						writer.Write(translation[i]);
						writer.Write("[/']");
						break;
					case '%':
						// Eeplace "%v"
						if (!WriteTilda(writer, translation, ref i))
							writer.Write(ch);

						break;
					case '*':
					case '|':
						// Skipping
						break;
					default:
						WriteEscaped(writer, ch);
						break;
				}
			}
		}

		private static void WriteEscaped(StreamWriter writer, string str)
		{
			foreach (char ch in str)
				WriteEscaped(writer, ch);
		}

		private static void WriteEscaped(StreamWriter writer, char ch)
		{
			switch (ch)
			{
				case '[':
				case ']':
				case '{':
				case '}':
				case '(':
				case ')':
				case '#':
				case '~':
				case '\\':
				case '^':
				case '@':
					writer.Write('\\');
					break;
			}

			writer.Write(ch);
		}

		private static bool WriteTilda(StreamWriter writer, string str, ref int i)
		{
			if (str[i + 1] == 'v')
			{
				writer.Write("\\~");
				++i;

				return true;
			}

			return false;
		}
	}
}
