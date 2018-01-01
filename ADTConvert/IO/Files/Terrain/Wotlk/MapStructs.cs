﻿using System.Runtime.InteropServices;

namespace ADTConvert.IO.Files.Terrain.Wotlk
{
    [StructLayout(LayoutKind.Sequential)]
    struct Mhdr
    {
        public uint Flags;
        public int ofsMcin;
        public int ofsMtex;
        public int ofsMmdx;
        public int ofsMmid;
        public int ofsMwmo;
        public int ofsMwid;
        public int ofsMddf;
        public int ofsModf;
        public int ofsMfbo;
        public int ofsMh2o;
        public int ofsMtxf;
        public int unused0, unused1, unused2, unused3;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Mcin
    {
        public int OfsMcnk;
        public int SizeMcnk;
        public int Flags;
        public int AsyncId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Mcnk
    {
        public uint Flags;
        public readonly int IndexX;
        public readonly int IndexY;
        public int NumLayers;
        public int NumDoodadRefs;
        public int Mcvt;
        public int Mcnr;
        public int Mcly;
        public int Mcrf;
        public int Mcal;
        public int SizeAlpha;
        public int Mcsh;
        public int SizeShadow;
        public int AreaId;
        public readonly int NumMapObjRefs;
        public int Holes;
        public readonly ulong Low1;
        public readonly ulong Low2;
        public readonly int PredTex;
        public readonly int NoEffectDoodad;
        public int Mcse;
        public int NumSoundEmitters;
        public int Mclq;
        public int SizeLiquid;
        public C3Vector Position;
        public int Mccv;
        public int Mclv;
        public readonly int Unused;
    }
}
