using System;
using System.IO;
namespace ServiceTool
{
 internal static class ExecutableArchitecture
 {
  internal static string Caption(string path)
  {try{return Read(path);}catch(Exception ex){if(ex is InvalidDataException||ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is System.Security.SecurityException)return "разрядность не определена";throw;}}
  internal static string Read(string path)
  {
   using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
   using(var reader=new BinaryReader(stream)){
    if(stream.Length<64||reader.ReadUInt16()!=0x5a4d)throw new InvalidDataException("Не PE-файл");
    stream.Position=60;uint pe=reader.ReadUInt32();if(pe>stream.Length-24)throw new InvalidDataException("Повреждён PE");
    stream.Position=pe;if(reader.ReadUInt32()!=0x4550)throw new InvalidDataException("Не PE-файл");
    ushort machine=reader.ReadUInt16(),sections=reader.ReadUInt16();stream.Position+=12;ushort size=reader.ReadUInt16();stream.Position+=2;
    long optional=stream.Position;if(size<96||optional+size>stream.Length||sections>96)throw new InvalidDataException("Повреждён PE");
    ushort magic=reader.ReadUInt16();int directories=magic==0x10b?96:magic==0x20b?112:0;if(directories==0)throw new InvalidDataException("Неизвестный PE");
    uint clr=0;if(size>=directories+15*8){stream.Position=optional+directories+14*8;clr=reader.ReadUInt32();}
    if(machine==0x14c&&clr!=0){
     long table=optional+size;
     if(table+sections*40>stream.Length)throw new InvalidDataException("Повреждены секции");
     for(int i=0;i<sections;i++){
      stream.Position=table+i*40+8;uint virtualSize=reader.ReadUInt32(),rva=reader.ReadUInt32(),rawSize=reader.ReadUInt32(),raw=reader.ReadUInt32();
      if(clr<rva||(ulong)clr-rva+20>rawSize)continue;
      long pos=(long)raw+clr-rva;if(pos<0||pos+20>stream.Length)throw new InvalidDataException("Повреждён CLR");
      stream.Position=pos+16;uint flags=reader.ReadUInt32();
      if((flags&2)!=0)return "32-разрядная";
      if((flags&1)==0)return "32-разрядная";
      return (flags&0x20000)!=0?"AnyCPU, предпочтительно 32-разрядная":"AnyCPU";
     }throw new InvalidDataException("CLR не найден");
    }
    return machine==0x14c?"32-разрядная":machine==0x8664?"64-разрядная":machine==0xaa64?"ARM64":"неподдерживаемая архитектура";
   }
  }
  internal static string FileCaption(string path){string value=Caption(path);if(value=="AnyCPU")value="универсальная разрядность службы: AnyCPU";else if(value=="32-разрядная")value="разрядность службы: x86";else if(value=="64-разрядная")value="разрядность службы: x64";return Path.GetFileName(path)+" - ("+value+")";}
 }
}
