﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BizHawk.Emulation.Cores.Waterbox
{
	/// <summary>
	/// a simple grow-only fixed max size heap
	/// </summary>
	internal sealed class Heap : IDisposable
	{
		public MemoryBlock Memory { get; private set; }
		/// <summary>
		/// name, used in identifying errors
		/// </summary>
		public string Name { get; private set; }
		/// <summary>
		/// total number of bytes used
		/// </summary>
		public ulong Used { get; private set; }

		/// <summary>
		/// true if the heap has been sealed, preventing further changes
		/// </summary>
		public bool Sealed { get; private set; }

		private byte[] _hash;

		public Heap(ulong start, ulong size, string name)
		{
			Memory = new MemoryBlock(start, size);
			Used = 0;
			Name = name;
		}

		private void EnsureAlignment(int align)
		{
			if (align > 1)
			{
				ulong newused = ((Used - 1) | (ulong)(align - 1)) + 1;
				if (newused > Memory.Size)
				{
					throw new InvalidOperationException(string.Format("Failed to meet alignment {0} on heap {1}", align, Name));
				}
				Used = newused;
			}
		}

		public ulong Allocate(ulong size, int align)
		{
			if (Sealed)
				throw new InvalidOperationException(string.Format("Attempt made to allocate from sealed heap {0}", Name));

			EnsureAlignment(align);

			ulong newused = Used + size;
			if (newused > Memory.Size)
			{
				throw new InvalidOperationException(string.Format("Failed to allocate {0} bytes from heap {1}", size, Name));
			}
			ulong ret = Memory.Start + Used;
			Memory.Protect(ret, newused - Used, MemoryBlock.Protection.RW);
			Used = newused;
			Console.WriteLine("Allocated {0} bytes on {1}", size, Name);
			return ret;
		}

		public void Seal()
		{
			if (!Sealed)
			{
				Memory.Protect(Memory.Start, Memory.Size, MemoryBlock.Protection.R);
				_hash = WaterboxUtils.Hash(Memory.GetStream(Memory.Start, Used, false));
				Sealed = true;
			}
			else
			{
				throw new InvalidOperationException(string.Format("Attempt to reseal heap {0}", Name));
			}
		}

		public void SaveStateBinary(BinaryWriter bw)
		{
			bw.Write(Name);
			bw.Write(Used);
			if (!Sealed)
			{
				var ms = Memory.GetStream(Memory.Start, Used, false);
				ms.CopyTo(bw.BaseStream);
			}
			else
			{
				bw.Write(_hash);
			}
		}

		public void LoadStateBinary(BinaryReader br)
		{
			var name = br.ReadString();
			if (name != Name)
				throw new InvalidOperationException(string.Format("Name did not match for heap {0}", Name));
			var used = br.ReadUInt64();
			if (used > Memory.Size)
				throw new InvalidOperationException(string.Format("Heap {0} used {1} larger than available {2}", Name, used, Memory.Size));
			if (!Sealed)
			{
				Memory.Protect(Memory.Start, Memory.Size, MemoryBlock.Protection.None);
				Memory.Protect(Memory.Start, used, MemoryBlock.Protection.RW);
				var ms = Memory.GetStream(Memory.Start, used, true);
				WaterboxUtils.CopySome(br.BaseStream, ms, (long)used);
				Used = used;
			}
			else
			{
				var hash = br.ReadBytes(_hash.Length);
				if (!hash.SequenceEqual(_hash))
				{
					throw new InvalidOperationException(string.Format("Hash did not match for heap {0}.  Is this the same rom?"));
				}
			}
		}

		public void Dispose()
		{
			if (Memory != null)
			{
				Memory.Dispose();
				Memory = null;
			}
		}
	}
}
