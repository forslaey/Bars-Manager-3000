using System;
using System.IO;
namespace ServiceTool
{
    internal static class DownloadTransfer
    {
        internal static long Copy(Stream input, Stream output, long length, long limit, Action cancel, Action<TransferProgress> report, string file)
        {
            long bytes = 0; var buffer = new byte[65536];
            Action publish = () => { if(report != null) report(new TransferProgress { Stage="Скачивание", File=file, Bytes=bytes, Total=Math.Max(0,length) }); };
            cancel(); publish();
            while(true)
            {
                cancel(); int count=input.Read(buffer,0,buffer.Length); cancel(); if(count==0) break;
                if(bytes > limit-count) throw new InvalidDataException("Превышен допустимый размер загрузки");
                output.Write(buffer,0,count); bytes+=count; publish();
            }
            if(length>=0 && bytes!=length) throw new IOException("Загрузка не завершена");
            cancel(); return bytes;
        }
    }
}
