#Requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Executable,
    [string] $IconFile = (Join-Path $PSScriptRoot '../Assets/Branding/chih-icon.ico'))
$ErrorActionPreference = 'Stop'
if (-not ('ChihIconResources' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
public static class ChihIconResources {
    delegate bool Names(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool EnumResourceNames(IntPtr module, IntPtr type, Names callback, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    static byte[] Read(IntPtr module, IntPtr name, int type) {
        var resource=FindResource(module,name,new IntPtr(type));
        if(resource==IntPtr.Zero) throw new InvalidDataException("Missing native icon resource");
        var bytes=new byte[SizeofResource(module,resource)];
        Marshal.Copy(LockResource(LoadResource(module,resource)),bytes,0,bytes.Length); return bytes;
    }
    public static int[] Verify(string executable,string ico) {
        var bytes=File.ReadAllBytes(ico);
        if(bytes.Length<6 || BitConverter.ToUInt16(bytes,0)!=0 || BitConverter.ToUInt16(bytes,2)!=1) throw new InvalidDataException("Invalid ICO");
        int count=BitConverter.ToUInt16(bytes,4); var expected=new List<string>(); var sizes=new List<int>();
        for(int i=0;i<count;i++) {
            int at=6+i*16; int length=checked((int)BitConverter.ToUInt32(bytes,at+8)), offset=checked((int)BitConverter.ToUInt32(bytes,at+12));
            expected.Add(Convert.ToHexString(SHA256.HashData(bytes.AsSpan(offset,length)))); sizes.Add(bytes[at]==0?256:bytes[at]);
        }
        var module=LoadLibraryEx(Path.GetFullPath(executable),IntPtr.Zero,0x22); // Data/resource only; never executes the file.
        if(module==IntPtr.Zero) throw new IOException("Cannot map executable resources");
        bool match=false; Exception failure=null;
        try {
            Names callback=(m,t,name,p)=> {
                try {
                    var group=Read(m,name,14); var hashes=new HashSet<string>();
                    for(int i=0;i<BitConverter.ToUInt16(group,4);i++) {
                        int id=BitConverter.ToUInt16(group,6+i*14+12);
                        hashes.Add(Convert.ToHexString(SHA256.HashData(Read(m,new IntPtr(id),3))));
                    }
                    if(expected.All(h=>hashes.Contains(h))) match=true;
                } catch(Exception ex) { failure=ex; }
                return true;
            };
            EnumResourceNames(module,new IntPtr(14),callback,IntPtr.Zero);
            GC.KeepAlive(callback);
            if(!match) throw new InvalidDataException("Approved ICO frame bytes are not embedded intact",failure);
            return sizes.ToArray();
        } finally { FreeLibrary(module); }
    }
}
'@
}
$sizes = [ChihIconResources]::Verify((Resolve-Path -LiteralPath $Executable).Path, (Resolve-Path -LiteralPath $IconFile).Path)
Write-Host "PASS: native PE icon contains approved frame bytes ($($sizes -join ', ') px): $Executable"
