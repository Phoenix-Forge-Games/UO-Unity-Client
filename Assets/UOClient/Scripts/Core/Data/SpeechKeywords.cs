using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UOClient.Data
{
    /// <summary>
    /// speech.mul: phrases NPCs listen for ("bank", "vendor buy", "guards"), each with a keyword ID. Speech sent to
    /// the server carries the IDs of the phrases it contains, and ServUO's NPCs react to the IDs, not the text.
    /// The matching rules follow ClassicUO / TazUO (SpeechesLoader, BSD 2-Clause, Copyright (c) 2025, andreakarasho).
    /// </summary>
    public sealed class SpeechKeywords
    {
        private readonly struct Entry
        {
            public readonly ushort Id;
            public readonly string[] Words;
            public readonly bool AnyStart, AnyEnd;

            public Entry(ushort id, string phrase)
            {
                Id = id;
                Words = phrase.Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries);
                AnyStart = phrase.Length > 0 && phrase[0] == '*';
                AnyEnd = phrase.Length > 0 && phrase[phrase.Length - 1] == '*';
            }
        }

        private readonly List<Entry> _entries = new List<Entry>();

        public SpeechKeywords(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            int position = 0;
            while (position + 4 <= data.Length)
            {
                ushort id = (ushort)((data[position] << 8) | data[position + 1]);
                int length = (data[position + 2] << 8) | data[position + 3];
                position += 4;
                if (position + length > data.Length)
                {
                    break;
                }

                if (length > 0)
                {
                    _entries.Add(new Entry(id, Encoding.UTF8.GetString(data, position, length)));
                }

                position += length;
            }
        }

        /// <summary>The keyword IDs in `text`, sorted and distinct.</summary>
        public List<ushort> Find(string text)
        {
            var found = new SortedSet<ushort>();
            text = text.Trim(' ');
            foreach (Entry entry in _entries)
            {
                if (IsMatch(text, entry))
                {
                    found.Add(entry.Id);
                }
            }

            return new List<ushort>(found);
        }

        private static bool IsMatch(string input, Entry entry)
        {
            foreach (string word in entry.Words)
            {
                if (word.Length == 0 || word.Length > input.Length)
                {
                    continue;
                }

                if (!entry.AnyStart && input.IndexOf(word, 0, word.Length, StringComparison.InvariantCultureIgnoreCase) == -1)
                {
                    continue;
                }

                if (!entry.AnyEnd && input.IndexOf(word, input.Length - word.Length, StringComparison.InvariantCultureIgnoreCase) == -1)
                {
                    continue;
                }

                int idx = input.IndexOf(word, StringComparison.InvariantCultureIgnoreCase);
                while (idx >= 0)
                {
                    bool startOk = idx == 0 || !char.IsLetter(input[idx - 1]);
                    int after = idx + word.Length;
                    bool endOk = after >= input.Length || !char.IsLetter(input[after]);
                    if (startOk && endOk)
                    {
                        return true;
                    }

                    idx = input.IndexOf(word, idx + 1, StringComparison.InvariantCultureIgnoreCase);
                }
            }

            return false;
        }
    }
}
