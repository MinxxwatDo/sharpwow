﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SlimDX.Direct3D9;
using SlimDX;

namespace SharpWoW.ADT.Wotlk
{
    public partial class ADTChunk : IADTChunk
    {
        public ADTChunk(ADTFile parent, Stormlib.MPQFile file, MCIN info)
        {
            mParent = parent;
            mFile = file;
            mInfo = info;
        }

        public bool ProcessSyncLoad()
        {
            return true;
        }

        public bool PreLoadChunk()
        {
            mFile.Position = mInfo.ofsMcnk;
            if (ReadSignature() != "MCNK")
                return false;

            uint size = mFile.Read<uint>();
            // +8 -> MCNK + size = 8 bytes
            if (size + 8 != mInfo.size)
                return false;

            mHeader = mFile.Read<MCNK>();
            var posY = (32.0f * Utils.Metrics.Tilesize - mHeader.position.X) - Utils.Metrics.MidPoint;
            var posX = (32.0f * Utils.Metrics.Tilesize - mHeader.position.Y) - Utils.Metrics.MidPoint;
            mHeader.position.X = posX;
            mHeader.position.Y = posY;

            mFile.Position = mInfo.ofsMcnk + mHeader.ofsHeight;
            if (ReadSignature() != "MCVT")
                return false;

            size = mFile.Read<uint>();
            float[] height = new float[145];
            uint counter = 0;
            Vector3 minPos = new Vector3(999999.9f);
            Vector3 maxPos = new Vector3(-999999.9f);
            minPos.X = mHeader.position.X;
            minPos.Y = mHeader.position.Y;
            maxPos.X = minPos.X + Utils.Metrics.Chunksize;
            maxPos.Y = minPos.Y + Utils.Metrics.Chunksize;
            for (int i = 0; i < 17; ++i)
            {
                for (int j = 0; j < (((i % 2) != 0) ? 8 : 9); ++j)
                {
                    float x, y, z;
                    z = mFile.Read<float>() + mHeader.position.Z;
                    y = i * Utils.Metrics.Unitsize * 0.5f + mHeader.position.Y;
                    x = j * Utils.Metrics.Unitsize + mHeader.position.X;

                    if ((i % 2) != 0)
                        x += 0.5f * Utils.Metrics.Unitsize;

                    if (z < minPos.Z)
                        minPos.Z = z;
                    if (z > maxPos.Z)
                        maxPos.Z = z;

                    vertices[counter] = new ADTVertex()
                    {
                        X = x,
                        Y = y,
                        Z = z,
                        U = ADTStaticData.TexCoords[counter, 0],
                        V = ADTStaticData.TexCoords[counter, 1],
                        S = ADTStaticData.AlphaCoords[counter, 0],
                        T = ADTStaticData.AlphaCoords[counter, 1]
                    };

                    ++counter;
                }
            }

            mBox = new BoundingBox(minPos, maxPos);
            MinPosition = minPos;
            MaxPosition = maxPos;

            mFile.Position = mInfo.ofsMcnk + mHeader.ofsLayer;
            if (ReadSignature() != "MCLY")
                return false;

            mFile.Read<uint>();

            for (int i = 0; i < mHeader.nLayers; ++i)
            {
                var layer = mFile.Read<MCLY>();
                mLayers.Add(layer);
                if ((layer.flags & 0x40) != 0)
                    mTextureFlags[i] = 1;
            }

            LoadAlphaData();
            if (!LoadNormals())
                return false;

            mFile.Position = mInfo.ofsMcnk + mHeader.ofsRefs + 0x08;
            for (uint i = 0; i < mHeader.nDoodadRefs; ++i)
                mRefs.Add(mFile.Read<uint>());

            return true;
        }

        private bool LoadNormals()
        {
            mFile.Position = mInfo.ofsMcnk + mHeader.ofsNormal;
            if (ReadSignature() != "MCNR")
                return false;

            uint size = mFile.Read<uint>();
            uint counter = 0;
            for (uint i = 0; i < 17; ++i)
            {
                for (uint j = 0; j < (((i % 2) != 0) ? 8u : 9u); ++j)
                {
                    float nx = -((float)(mFile.Read<sbyte>()) / 127.0f);
                    float ny = -((float)(mFile.Read<sbyte>()) / 127.0f);
                    float nz = (float)(mFile.Read<sbyte>()) / 127.0f;
                    vertices[counter].NX = nx;
                    vertices[counter].NY = ny;
                    vertices[counter++].NZ = nz;
                }
            }

            return true;
        }

        private bool LoadAlphaData()
        {
            byte[,] alphaData = new byte[4096, 4];
            for (int i = 0; i < 64; ++i)
            {
                for (int j = 0; j < 64; ++j)
                {
                    float x = i * ADTStaticData.HoleSize;
                    float y = j * ADTStaticData.HoleSize;
                    uint stepx = (uint)Math.Floor(x / ADTStaticData.HoleLen);
                    uint stepy = (uint)Math.Floor(y / ADTStaticData.HoleLen);

                    byte factor = (byte)((mHeader.holes & (ADTStaticData.HoleBitmap[stepx, stepy])) != 0 ? 0 : 1);
                    alphaData[i * 64 + j, 3] = (byte)(0xFF * factor);
                    //AlphaFloats[i * 64 + j, 3] = 0xFF * factor;
                    AlphaData[(i * 64 + j) * 4 + 3] = (byte)(0xFF * factor);
                }
            }

            for (int i = 1; i < mHeader.nLayers; ++i)
            {
                mFile.Position = mInfo.ofsMcnk + mHeader.ofsAlpha + 0x08 + mLayers[i].offsetMCAL;
                byte[] fileData = mFile.Read(2048);

                uint bufferPtr = 0;
                uint mapPtr = 0;
                for (int j = 0; j < 64; j++)
                {
                    for (int k = 0; k < 32; k++)
                    {

                        float x = i * ADTStaticData.HoleSize * 2;
                        float y = j * ADTStaticData.HoleSize;
                        uint stepx = (uint)Math.Floor(x / ADTStaticData.HoleLen);
                        uint stepy = (uint)Math.Floor(y / ADTStaticData.HoleLen);

                        byte factor = (byte)((mHeader.holes & (ADTStaticData.HoleBitmap[stepx, stepy])) != 0 ? 0 : 1);
                        x += ADTStaticData.HoleSize;
                        stepx = (uint)Math.Floor(x / ADTStaticData.HoleLen);
                        byte factor2 = (byte)((mHeader.holes & (ADTStaticData.HoleBitmap[stepx, stepy])) != 0 ? 0 : 1);

                        alphaData[mapPtr, i - 1] = (byte)((((255 * ((int)(fileData[bufferPtr] & 0x0F))) / 0x0F)) * factor);
                        //AlphaFloats[mapPtr, i - 1] = (float)(((255 * ((int)(fileData[bufferPtr] & 0x0F))) / 0x0F)) * factor;
                        AlphaData[mapPtr * 4 + i - 1] = alphaData[mapPtr, i - 1];
                        ++mapPtr;
                        alphaData[mapPtr, i - 1] = (byte)((((255 * ((int)(fileData[bufferPtr++] & 0xF0))) / 0xF0)) * factor2);
                        //AlphaFloats[mapPtr, i - 1] = (float)(((255 * ((int)(fileData[bufferPtr++] & 0xF0))) / 0xF0)) * factor2;
                        AlphaData[mapPtr * 4 + i - 1] = alphaData[mapPtr, i - 1];
                        ++mapPtr;
                    }
                }
            }

            return true;
        }

        public void Unload()
        {
            if (mMesh != null || mAlphaTexture != null)
            {
                Game.GameManager.GraphicsThread.CallOnThread(
                    () =>
                    {
                        if (mAlphaTexture != null)
                            ADTAlphaHandler.AddFreeTexture(mAlphaTexture);

                        if (mMesh != null)
                        {
                            mMesh.Dispose();
                            mMesh = null;
                        }
                    },
                    true
                );
            }

            mParent = null;
            mFile = null;
            mLayers.Clear();
            mLayers = null;
            AlphaData = null;
            AlphaFloats = null;
        }

        public void Render()
        {
            if (Game.GameManager.GraphicsThread.GraphicsManager.Camera.ViewFrustum.Contains(mBox, Matrix.Identity) == ContainmentType.Disjoint)
                return;

            if (mHeader.nLayers == 0)
                return;

            if (mMesh == null)
                LoadMesh();

            if (mAlphaTexture == null)
                LoadAlphaTexture();

            foreach (var re in mRefs)
            {
                try
                {
                    var name = mParent.DoodadNames[mParent.ModelIdentifiers[(int)mParent.ModelDefinitions[(int)re].idMMID]];
                    var id = Game.GameManager.M2ModelManager.AddInstance(name, mParent.ModelDefinitions[(int)re]);
                    mDoodadInstances.Add(id);
                }
                catch (Exception)
                {
                }
            }

            mRefs.Clear();

            var shdr = Video.ShaderCollection.TerrainShader;
            shdr.SetTechnique(mHeader.nLayers - 1);
            shdr.SetTexture("alphaTexture", mAlphaTexture);
            for(int i = 0; i < 4; ++i)
                shdr.SetValue("TextureFlags" + i, mTextureFlags[i]);
            for (int i = 0; i < mLayers.Count; ++i)
                shdr.SetTexture("blendTexture" + i, mParent.GetTexture((int)mLayers[i].textureId));

            shdr.DoRender((SlimDX.Direct3D9.Device d) =>
                {
                    mMesh.DrawSubset(0);
                }
            );
        }

        private void LoadMesh()
        {
            mMesh = new Mesh(Game.GameManager.GraphicsThread.GraphicsManager.Device,
                256, 145, MeshFlags.Managed, ADTVertex.FVF);

            var vb = mMesh.LockVertexBuffer(LockFlags.None);
            vb.WriteRange(vertices);
            mMesh.UnlockVertexBuffer();

            var ib = mMesh.LockIndexBuffer(LockFlags.None);
            ib.WriteRange(ADTStaticData.Indices);
            mMesh.UnlockIndexBuffer();
        }

        private void LoadAlphaTexture()
        {
            mAlphaTexture = ADTAlphaHandler.FreeTexture();
            if (mAlphaTexture == null)
                mAlphaTexture = new SlimDX.Direct3D9.Texture(Game.GameManager.GraphicsThread.GraphicsManager.Device, 64, 64, 1, Usage.None, Format.A8R8G8B8, Pool.Managed);
            Surface baseSurf = mAlphaTexture.GetSurfaceLevel(0);
            System.Drawing.Rectangle rec = System.Drawing.Rectangle.FromLTRB(0, 0, 64, 64);
            Surface.FromMemory(baseSurf, AlphaData, Filter.None, 0, Format.A8R8G8B8, 4 * 64, rec);
            baseSurf.Dispose();
        }

        private void RecalcNormals()
        {
            for(uint i = 0; i < 145; ++i)
            {
                Vector3 N1, N2, N3, N4;
                Vector3 P1, P2, P3, P4;

                P1.X = vertices[i].X - Utils.Metrics.Unitsize * 0.5f;
                P1.Y = vertices[i].Y - Utils.Metrics.Unitsize * 0.5f;
                P1.Z = vertices[i].Z;
                Game.GameManager.WorldManager.GetLandHeightFast(P1.X, P1.Y, ref P1.Z);

                P2.X = vertices[i].X + Utils.Metrics.Unitsize * 0.5f;
                P2.Y = vertices[i].Y - Utils.Metrics.Unitsize * 0.5f;
                P2.Z = vertices[i].Z;
                Game.GameManager.WorldManager.GetLandHeightFast(P2.X, P2.Y, ref P2.Z);

                P3.X = vertices[i].X + Utils.Metrics.Unitsize * 0.5f;
                P3.Y = vertices[i].Y + Utils.Metrics.Unitsize * 0.5f;
                P3.Z = vertices[i].Z;
                Game.GameManager.WorldManager.GetLandHeightFast(P3.X, P3.Y, ref P3.Z);

                P4.X = vertices[i].X - Utils.Metrics.Unitsize * 0.5f;
                P4.Y = vertices[i].Y + Utils.Metrics.Unitsize * 0.5f;
                P4.Z = vertices[i].Z;
                Game.GameManager.WorldManager.GetLandHeightFast(P4.X, P4.Y, ref P4.Z);

                Vector3 vert = new Vector3(vertices[i].X, vertices[i].Y, vertices[i].Z);

                N1 = Vector3.Cross((P2 - vert), (P1 - vert));
                N2 = Vector3.Cross((P3 - vert), (P2 - vert));
                N3 = Vector3.Cross((P4 - vert), (P3 - vert));
                N4 = Vector3.Cross((P1 - vert), (P4 - vert));

                var Norm = N1 + N2 + N3 + N4;
                Norm.Normalize();
                Norm *= -1;

                vertices[i].NX = Norm.X;
                vertices[i].NY = Norm.Y;
                vertices[i].NZ = Norm.Z;
            }
        }

        public Vector3 MinPosition { get; private set; }
        public Vector3 MaxPosition { get; private set; }

        private ADTFile mParent;
        private Stormlib.MPQFile mFile;
        private MCIN mInfo;
        private Mesh mMesh;
        private List<MCLY> mLayers = new List<MCLY>();
        private byte[] AlphaData = new byte[4096 * 4];
        private short[,] AlphaFloats = new short[4096, 3];
        private ADTVertex[] vertices = new ADTVertex[145];
        private BoundingBox mBox;
        private Texture mAlphaTexture = null;
        private int[] mTextureFlags = new int[4] { 0, 0, 0, 0 };
        private List<uint> mRefs = new List<uint>();
        private List<uint> mDoodadInstances = new List<uint>();

        private string ReadSignature()
        {
            byte[] bytes = mFile.Read(4);
            bytes = bytes.Reverse().ToArray();
            return Encoding.UTF8.GetString(bytes);
        }
    }
}