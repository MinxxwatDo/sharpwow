﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpWoW.Video
{
    public class TextureManager
    {
        private Dictionary<int, TextureHandle> mTextures = new Dictionary<int, TextureHandle>();
        private Dictionary<TextureHandle, int> mRefCounts = new Dictionary<TextureHandle, int>();
        private object lockObject = new object();
        private SlimDX.Direct3D9.Device mDevice;

        private static TextureManager mDefaultManager = new TextureManager(Game.GameManager.GraphicsThread.GraphicsManager.Device);

        public SlimDX.Direct3D9.Device AssociatedDevice { get { return mDevice; } }
        public static TextureManager Default { get { return mDefaultManager; } }
        public TextureHandle ErrorTexture { get; private set; }

        public TextureManager(SlimDX.Direct3D9.Device dev)
        {
            mDevice = dev;
            ErrorTexture = fromMemoryGDI(Resources.Images.Error);
        }

        private TextureHandle fromMemoryGDI(System.Drawing.Bitmap bmp)
        {
            var locked = bmp.LockBits(new System.Drawing.Rectangle(System.Drawing.Point.Empty, bmp.Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var texRet = new SlimDX.Direct3D9.Texture(mDevice, bmp.Width, bmp.Height, 1, SlimDX.Direct3D9.Usage.None, SlimDX.Direct3D9.Format.A8R8G8B8, SlimDX.Direct3D9.Pool.Managed);
            var surf = texRet.LockRectangle(0, SlimDX.Direct3D9.LockFlags.None);
            surf.Data.WriteRange(locked.Scan0, locked.Stride * locked.Height);
            texRet.UnlockRectangle(0);

            bmp.UnlockBits(locked);

            return new TextureHandle(texRet);
        }

        private TextureHandle _GetTexture(string texture)
        {
            lock (lockObject)
            {
                int hash = texture.GetHashCode();
                if (mTextures.ContainsKey(hash))
                {
                    var retVal = mTextures[hash];
                    mRefCounts[retVal]++;
                    return retVal;
                }

                var ret = _LoadTexture(texture);
                ret.Name = texture;
                if (ret == null)
                    throw new InvalidOperationException(texture + " is not a valid texture!");

                mTextures.Add(hash, ret);
                mRefCounts.Add(ret, 1);
                return ret;
            }
        }

        private void _RemoveTexture(TextureHandle handle)
        {
            lock (lockObject)
            {
                if (mRefCounts.ContainsKey(handle) == false)
                    return;

                --mRefCounts[handle];
                if (mRefCounts[handle] == 0)
                {
                    Game.GameManager.GraphicsThread.CallOnThread(() => handle.Native.Dispose(), true);
                    mRefCounts.Remove(handle);
                    mTextures.Remove(handle.Name.GetHashCode());
                }
            }
        }

        public TextureHandle LoadTexture(string texture)
        {
            return _GetTexture(texture);
        }

        public void UnloadTexture(TextureHandle handle)
        {
            _RemoveTexture(handle);
        }

        public static TextureHandle GetTexture(string texture)
        {
            return mDefaultManager._GetTexture(texture);
        }

        public static void RemoveTexture(TextureHandle handle)
        {
            mDefaultManager._RemoveTexture(handle);
        }

        private TextureHandle _LoadTexture(string name)
        {
            var device = mDevice;
            Stormlib.MPQFile fl = new Stormlib.MPQFile(name);
            System.IO.BinaryReader reader = new System.IO.BinaryReader(fl);
            uint sig = reader.ReadUInt32();
            if (sig == 0x32504C42)
            {
                return LoadBlpTexture(device, reader);
            }
            try
            {
                var tex = SlimDX.Direct3D9.Texture.FromStream(device, fl);
                if (tex != null)
                    return new TextureHandle(tex);
            }
            catch (Exception)
            {
            }

            return null;
        }

        private TextureHandle LoadBlpTexture(SlimDX.Direct3D9.Device Render, System.IO.BinaryReader reader)
        {
            reader.BaseStream.Position += 4;
            byte compression = reader.ReadByte();
            byte alphaDepth = reader.ReadByte();
            byte alphaEncoding = reader.ReadByte();
            byte hasMipMap = reader.ReadByte();
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int[] Offsets = new int[16];
            int[] Sizes = new int[16];
            byte[] ofsTmp = reader.ReadBytes(16 * 4);
            byte[] sizTmp = reader.ReadBytes(16 * 4);
            int levelCount = 0;
            int blockSize = 0;
            for (int i = 0; i < 16; ++i)
            {
                Offsets[i] = BitConverter.ToInt32(ofsTmp, 4 * i);
                Sizes[i] = BitConverter.ToInt32(sizTmp, 4 * i);
                if (Offsets[i] != 0 && Sizes[i] != 0)
                    ++levelCount;
            }

            SlimDX.Direct3D9.Format texFmt = SlimDX.Direct3D9.Format.Unknown;
            if (compression == 2)
            {
                switch (alphaEncoding)
                {
                    case 0:
                        texFmt = SlimDX.Direct3D9.Format.Dxt1;
                        blockSize = 2;
                        break;
                    case 1:
                        texFmt = SlimDX.Direct3D9.Format.Dxt3;
                        blockSize = 4;
                        break;
                    case 7:
                        texFmt = SlimDX.Direct3D9.Format.Dxt5;
                        blockSize = 4;
                        break;
                }
            }

            if (compression == 3)
            {
                texFmt = SlimDX.Direct3D9.Format.A8R8G8B8;
                blockSize = 4;
            }

            if (texFmt == SlimDX.Direct3D9.Format.Unknown)
                throw new FormatException("This format is not yet supported, sorry!");

            var texture = new SlimDX.Direct3D9.Texture(Render, width, height, levelCount,
                SlimDX.Direct3D9.Usage.None, texFmt, SlimDX.Direct3D9.Pool.Managed);
            int curLevel = 0;

            for (int i = 0; i < 16; ++i)
            {
                if (Sizes[i] != 0 && Offsets[i] != 0)
                {
                    reader.BaseStream.Position = Offsets[i];
                    byte[] layerData = reader.ReadBytes(Sizes[i]);
                    SlimDX.Direct3D9.Surface surf = texture.GetSurfaceLevel(curLevel);
                    SlimDX.Direct3D9.SurfaceDescription desc = texture.GetLevelDescription(curLevel);
                    System.Drawing.Rectangle rec = System.Drawing.Rectangle.FromLTRB(0, 0, desc.Width, desc.Height);
                    SlimDX.Direct3D9.Surface.FromMemory(surf, layerData, SlimDX.Direct3D9.Filter.Triangle, 0, texFmt, blockSize * rec.Width, rec);
                    ++curLevel;
                }
            }

            return new TextureHandle(texture);
        }
    }
}
