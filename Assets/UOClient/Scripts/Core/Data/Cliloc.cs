using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UOClient.Data
{
    /// <summary>
    /// Cliloc.enu, the client's numbered message table. The server sends most system messages, names and menu
    /// entries as a number plus tab-separated arguments (0xC1 / 0xCC); <see cref="Format"/> turns those into text.
    /// Newer clients ship the file BWT-compressed (a 0x8E fourth byte); <see cref="BwtDecompress"/> unpacks it.
    /// </summary>
    public sealed class Cliloc
    {
        private readonly Dictionary<int, string> _entries = new Dictionary<int, string>();

        public Cliloc(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length > 3 && data[3] == 0x8E)
            {
                data = BwtDecompress.Decompress(data);
            }

            int position = 6; // int32 header + int16 header
            while (position + 7 <= data.Length)
            {
                int number = BitConverter.ToInt32(data, position);
                int length = BitConverter.ToInt16(data, position + 5);
                position += 7;
                if (length < 0 || position + length > data.Length)
                {
                    break;
                }

                _entries[number] = Encoding.UTF8.GetString(data, position, length);
                position += length;
            }
        }

        public int Count => _entries.Count;

        public string Get(int number) => _entries.TryGetValue(number, out string text) ? text : null;

        /// <summary>
        /// The message for `number` with its `~1_NAME~` placeholders filled from `args` (tab-separated; an argument
        /// starting with '#' is itself a cliloc number).
        /// </summary>
        public string Format(int number, string args)
        {
            string text = Get(number);
            if (text == null)
            {
                return string.IsNullOrEmpty(args) ? $"[{number}]" : $"[{number}] {args.Replace('\t', ' ')}";
            }

            string[] parts = string.IsNullOrEmpty(args) ? Array.Empty<string>() : args.Split('\t');
            var sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('~', i);
                int close = open >= 0 ? text.IndexOf('~', open + 1) : -1;
                if (open < 0 || close < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }

                sb.Append(text, i, open - i);
                string token = text.Substring(open + 1, close - open - 1);
                int underscore = token.IndexOf('_');
                if (int.TryParse(underscore >= 0 ? token.Substring(0, underscore) : token, out int index) && index >= 1)
                {
                    string arg = index - 1 < parts.Length ? parts[index - 1] : string.Empty;
                    if (arg.Length > 1 && arg[0] == '#' && int.TryParse(arg.Substring(1), out int nested))
                    {
                        arg = Get(nested) ?? arg;
                    }

                    sb.Append(arg);
                }
                else
                {
                    sb.Append('~').Append(token).Append('~');
                }

                i = close + 1;
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// The Burrows-Wheeler (move-to-front) decompression used by newer Cliloc files.
    /// Ported from ClassicUO / TazUO (ClassicUO.Utility.BwtDecompress), BSD 2-Clause License,
    /// Copyright (c) 2025, andreakarasho. All rights reserved.
    /// </summary>
    internal static class BwtDecompress
    {
        public static byte[] Decompress(byte[] buffer)
        {
            // Undo the move-to-front step (the first four bytes are a header).
            var table = new ushort[256];
            for (int j = 0; j < 256; j++)
            {
                table[j] = (ushort)j;
            }

            var list = new byte[buffer.Length - 4];
            int i = 0;
            int position = 4;
            byte firstChar = buffer[position++];
            // As in the original: the byte read last is never decoded.
            while (position < buffer.Length)
            {
                byte current = firstChar;
                ushort value = table[current];
                for (int k = current; k > 0; k--)
                {
                    table[k] = table[k - 1];
                }

                table[0] = value;
                list[i++] = (byte)value;
                firstChar = buffer[position++];
            }

            return Inverse(list);
        }

        private static byte[] Inverse(byte[] input)
        {
            var symbolTable = new char[256];
            var frequency = new char[256];
            var partial = new int[256 * 3];
            for (int i = 0; i < 256; i++)
            {
                symbolTable[i] = (char)i;
            }

            Buffer.BlockCopy(input, 0, partial, 0, 1024); // 256 little-endian counts

            int sum = 0;
            for (int i = 0; i < 256; i++)
            {
                sum += partial[i];
            }

            uint len = (uint)sum;
            var output = new byte[len];
            int nonZero = 0;
            for (int i = 0; i < 256; i++)
            {
                if (partial[i] != 0)
                {
                    nonZero++;
                }
            }

            Frequency(partial, frequency);
            for (int i = 0, m = 0; i < nonZero; ++i)
            {
                byte freq = (byte)frequency[i];
                symbolTable[input[m + 1024]] = (char)freq;
                partial[freq + 256] = m + 1;
                m += partial[freq];
                partial[freq + 512] = m;
            }

            byte val = (byte)symbolTable[0];
            int count = 0;
            while (count < len)
            {
                output[count] = val;
                int first = partial[val + 256];
                if (first >= partial[val + 512])
                {
                    if (nonZero-- > 0)
                    {
                        ShiftLeft(symbolTable, nonZero);
                        val = (byte)symbolTable[0];
                    }
                }
                else
                {
                    char idx = (char)input[first + 1024];
                    partial[val + 256] = first + 1;
                    if (idx != 0)
                    {
                        ShiftLeft(symbolTable, idx);
                        symbolTable[(byte)idx] = (char)val;
                        val = (byte)symbolTable[0];
                    }
                }

                count++;
            }

            return output;
        }

        private static void Frequency(int[] input, char[] output)
        {
            var tmp = new int[256];
            Array.Copy(input, tmp, 256);
            for (int i = 0; i < 256; i++)
            {
                uint value = 0;
                byte index = 0;
                for (int j = 0; j < 256; j++)
                {
                    if (tmp[j] > value)
                    {
                        index = (byte)j;
                        value = (uint)tmp[j];
                    }
                }

                if (value == 0)
                {
                    break;
                }

                output[i] = (char)index;
                tmp[index] = 0;
            }
        }

        private static void ShiftLeft(char[] input, int max)
        {
            for (int i = 0; i < max; ++i)
            {
                input[i] = input[i + 1];
            }
        }
    }
}
