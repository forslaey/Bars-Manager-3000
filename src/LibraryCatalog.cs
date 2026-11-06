using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml;
using System.Text;
namespace ServiceTool
{
    internal enum LibraryKind { Msi, Inno, DotNet, Framework, Access, VcBundle, Composite, VcLegacy }
    internal sealed class LibraryPackage
    {
        public string Id, Name, Group, RelativePath, Version, Architecture, Sha256, ProductCode, UpgradeCode, RuntimeFamily, UninstallKey;
        public LibraryKind Kind;
        public int Order;
        public string FrameworkVersion = "4.8.1";
        public bool Preview;
        public bool Interactive { get { return false; } }
        public bool Recommended { get { return !Preview && Kind != LibraryKind.Composite; } }
    }
    internal static class LibraryCatalog
    {
        private static readonly LibraryPackage[] Entries = new LibraryPackage[]
        {
            new LibraryPackage { Id = "p01", Name = @".NET Framework 4.8.1 Developer Pack", Group = @".NET Framework", RelativePath = @"1.NDP481-DevPack-ENU.exe", Version = @"4.8.9256.5", Architecture = "both", Kind = LibraryKind.Framework, Sha256 = "FD9AE4B63CBC8F7637249C4B29D25281B1F70FBE6D650109DCBF2BDF596CF994", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"", UninstallKey = @"", Order = 0, Preview = false },
            new LibraryPackage { Id = "p02", Name = @"SQL Server System CLR Types, разрядность x64", Group = @"SQL Server", RelativePath = @"5.SQLSysClrTypes_x64.msi", Version = @"10.1.2531.0", Architecture = "x64", Kind = LibraryKind.Msi, Sha256 = "C54A2807EE849016E75DC759FB086E7011EA8F0BE62990FD6C26E29387A969BA", ProductCode = @"{2D766E70-7670-41A8-B370-1E09084ABA5D}", UpgradeCode = @"{CB56D685-7C8F-497E-81F5-D2F784CB00D9}", RuntimeFamily = @"", UninstallKey = @"", Order = 1, Preview = false },
            new LibraryPackage { Id = "p03", Name = @"DevExpress 13.2.7", Group = @"DevExpress", RelativePath = @"11.DevExpress_v13.2.7.exe", Version = @"13.2.7", Architecture = "both", Kind = LibraryKind.Inno, Sha256 = "3EE4CB5C220C9D072060136176304C0D1FD502D2D240AFEFBD13029DCF249ABB", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"", UninstallKey = @"Библиотеки DevExpress 2013.2.7_is1", Order = 10, Preview = false },
            new LibraryPackage { Id = "p04", Name = @"DevExpress 15.1.4", Group = @"DevExpress", RelativePath = @"12.DevExpress_v15.1.4.exe", Version = @"15.1.4", Architecture = "both", Kind = LibraryKind.Inno, Sha256 = "943CE91DD615964F5AF573A76E23CA392B058C334EDBE36062E879692441A5EB", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"", UninstallKey = @"Библиотеки DevExpress 15.1.4_is1", Order = 11, Preview = false },
            new LibraryPackage { Id = "p05", Name = @"DevExpress 22.1.4", Group = @"DevExpress", RelativePath = @"13.DevExpress_v22.1.4.exe", Version = @"22.1.4", Architecture = "both", Kind = LibraryKind.Inno, Sha256 = "39601933051D895E4F0CD349E1463918915CDBD973862D7231F9B72C48B30AF5", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"", UninstallKey = @"Библиотеки DevExpress 22.1.4_is1", Order = 12, Preview = false },
            new LibraryPackage { Id = "p06", Name = @"Microsoft SQL Server 2008 Native Client, разрядность x64", Group = @"SQL Server", RelativePath = @"14.sqlncli_x64.msi", Version = @"10.1.2531.0", Architecture = "x64", Kind = LibraryKind.Msi, Sha256 = "CC3C4F0239E87AFFE4B14A9DCEFDD3A10C4F74B2EB8098D9966E36C7651E4599", ProductCode = @"{BBDE8A3D-64A2-43A6-95F3-C27B87DF7AC1}", UpgradeCode = @"{D57692E4-C5D5-4752-A427-B61DD899D911}", RuntimeFamily = @"", UninstallKey = @"", Order = 13, Preview = false },
            new LibraryPackage { Id = "p07", Name = @"MSXML 6.0 Parser, разрядность x64", Group = @"Системные компоненты", RelativePath = @"15.msxml6_x64.msi", Version = @"6.10.1129.0", Architecture = "x64", Kind = LibraryKind.Msi, Sha256 = "7806740C262FDA66432C4981D6086E5B7220CE4E329E8FE6100FCF72A1FB1CF5", ProductCode = @"{CB2D89EE-29A3-4A54-A81E-3BC9A4FEA2B7}", UpgradeCode = @"{5BBED1F8-E6F3-4A02-BC97-26D35BE200CA}", RuntimeFamily = @"", UninstallKey = @"", Order = 14, Preview = false },
            new LibraryPackage { Id = "p08", Name = @"MSXML 6.0 Parser, разрядность x86", Group = @"Системные компоненты", RelativePath = @"16.msxml6_x86.msi", Version = @"6.10.1129.0", Architecture = "x86", Kind = LibraryKind.Msi, Sha256 = "893C49066EF1C5987E8F683634776D13C7A8D4FD5A6356C6823E4A108CCA3CE0", ProductCode = @"{8E719AE4-286B-4F01-8DA1-6270B0BF819D}", UpgradeCode = @"{1B117BA7-5BC1-419E-820E-7D4F3F412C7B}", RuntimeFamily = @"", UninstallKey = @"", Order = 15, Preview = false },
            new LibraryPackage { Id = "p09", Name = @"Access Database Engine 2016, разрядность x64", Group = @"Системные компоненты", RelativePath = @"17.accessdatabaseengine_X64.exe", Version = @"16.0.5044.1000", Architecture = "x64", Kind = LibraryKind.Access, Sha256 = "04E96C9F1A1F7D251A88AECECF1DC10FF65950392787427C00814A43308003DE", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"", UninstallKey = @"", Order = 16, Preview = false },
            new LibraryPackage { Id = "p10", Name = @"Microsoft Visual FoxPro OLE DB Provider, разрядность x86", Group = @"Системные компоненты", RelativePath = @"18.VFPOLEDBSetup.msi", Version = @"1.0.0", Architecture = "x86", Kind = LibraryKind.Msi, Sha256 = "741A707A3B2535D166CBE4E29C4B267F614F23514E04782B79F8E7B8C822C736", ProductCode = @"{3DA245C5-23B1-4874-BFA7-287B7D6C1EF6}", UpgradeCode = @"{0328ABAE-3F92-46F6-B141-91AF58A11DE1}", RuntimeFamily = @"", UninstallKey = @"", Order = 17, Preview = false },
            new LibraryPackage { Id = "p11", Name = @"Microsoft System CLR Types for SQL Server 2014, разрядность x64", Group = @"SQL Server", RelativePath = @"2.SQLSysClrTypes_2014_x64.msi", Version = @"12.0.2402.11", Architecture = "x64", Kind = LibraryKind.Msi, Sha256 = "14E13D88A76A2C05F90ED3F53A2C3C7CBC5593D3109E8BACDB45C12AB557A07F", ProductCode = @"{68BA34E8-9B9D-4A74-83F0-7D366B532D75}", UpgradeCode = @"{34CE963B-6DCD-437E-B75C-CF71A8E2FE77}", RuntimeFamily = @"", UninstallKey = @"", Order = 2, Preview = false },
            new LibraryPackage { Id = "p12", Name = @"Microsoft System CLR Types for SQL Server 2014, разрядность x86", Group = @"SQL Server", RelativePath = @"3.SQLSysClrTypes_2014.msi", Version = @"12.0.2402.11", Architecture = "x86", Kind = LibraryKind.Msi, Sha256 = "D99D572BAAE44CC5B79A1759761E1D1F0491382F64CFB547D2EFBE56CD01CC84", ProductCode = @"{718FFB65-F6E4-4D62-861F-ED10ED32C936}", UpgradeCode = @"{E066D1A6-CDAF-4613-9B18-8C25C84DBDCB}", RuntimeFamily = @"", UninstallKey = @"", Order = 3, Preview = false },
            new LibraryPackage { Id = "p13", Name = @"SQL Server System CLR Types, разрядность x86", Group = @"SQL Server", RelativePath = @"4.SQLSysClrTypes.msi", Version = @"10.1.2531.0", Architecture = "x86", Kind = LibraryKind.Msi, Sha256 = "E9C0FBFF2E627582254DABCF3BB5384057A269A7E71DA4C87BDB5905670AEA78", ProductCode = @"{C9FD9DF2-D92B-4321-A338-52961FECE249}", UpgradeCode = @"{44F931D8-47DD-4DA7-9612-DFBD98056DF4}", RuntimeFamily = @"", UninstallKey = @"", Order = 4, Preview = false },
            new LibraryPackage { Id = "p14", Name = @"Microsoft SQL Server 2008 R2 Management Objects, разрядность x86", Group = @"SQL Server", RelativePath = @"6.SharedManagementObjects.msi", Version = @"10.50.1600.1", Architecture = "x86", Kind = LibraryKind.Msi, Sha256 = "4B1EC03BC5CC69481B9DA6B2DB41AE6D3ADEACFFE854CC209D125E83A739F937", ProductCode = @"{B692E59A-055C-43B7-BE0A-9C2FE0AB88B6}", UpgradeCode = @"{6B87618C-BA3D-4CE5-BE4C-CEE56BF96D29}", RuntimeFamily = @"", UninstallKey = @"", Order = 5, Preview = false },
            new LibraryPackage { Id = "p15", Name = @"Microsoft SQL Server 2014 Management Objects, разрядность x86", Group = @"SQL Server", RelativePath = @"7.SharedManagementObjects_2014.msi", Version = @"12.0.2000.8", Architecture = "x86", Kind = LibraryKind.Msi, Sha256 = "654E7A016340A262F63F143984DCEF42C85C06AA92671BCAC22508F7C5B7BA41", ProductCode = @"{2774595F-BC2A-4B12-A25B-0C37A37049B0}", UpgradeCode = @"{87DFB838-5C35-4CE5-826E-3E21275D32F7}", RuntimeFamily = @"", UninstallKey = @"", Order = 6, Preview = false },
            new LibraryPackage { Id = "p16", Name = @"Microsoft SQL Server 2014 Management Objects  (x64), разрядность x64", Group = @"SQL Server", RelativePath = @"8.SharedManagementObjects_2014_x64.msi", Version = @"12.0.2000.8", Architecture = "x64", Kind = LibraryKind.Msi, Sha256 = "A44DE01F0554962C2D9E80C947FF3918FADD2E5F4B9C5D1AFA523D6E84A37332", ProductCode = @"{1F9EB3B6-AED7-4AA7-B8F1-8E314B74B2A5}", UpgradeCode = @"{A85FACB4-80AB-414C-BC73-619E9F8783E5}", RuntimeFamily = @"", UninstallKey = @"", Order = 7, Preview = false },
            new LibraryPackage { Id = "p17", Name = @"Microsoft SQL Server 2008 R2 Management Objects (x64), разрядность x64", Group = @"SQL Server", RelativePath = @"9.SharedManagementObjectsx64.msi", Version = @"10.50.1600.1", Architecture = "x64", Kind = LibraryKind.Msi, Sha256 = "DCCEC315E3C345A7EFB5951F3E9B27512B4E91D73EC48C7196633B7449115B7C", ProductCode = @"{F10ADDB9-839B-448B-BD2E-3BCB5C1E4B55}", UpgradeCode = @"{C82FE062-4045-408A-85A0-2D13FCAE67BC}", RuntimeFamily = @"", UninstallKey = @"", Order = 8, Preview = false },
            new LibraryPackage { Id = "p18", Name = @"DevExpress 8.2", Group = @"DevExpress", RelativePath = @"10.DevExpress_v8.2.exe", Version = @"8.2", Architecture = "both", Kind = LibraryKind.Inno, Sha256 = "987684AE74E8D1D726E402E69E54FBABC01E635B4C0576C3C72C181270BFCF18", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"", UninstallKey = @"Библиотеки DevExpress_is1", Order = 9, Preview = false },
            new LibraryPackage { Id = "p19", Name = @"ASP.NET Core 10.0.12, разрядность x64", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 10\aspnetcore-runtime-10.0.12-win-x64.exe", Version = @"10.0.12", Architecture = "x64", Kind = LibraryKind.DotNet, Sha256 = "E5D20698EA8CDDA55BCF8D6151D5B65097761506A1D8ED9AF39910CFC956E5AD", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.AspNetCore.App", UninstallKey = @"", Order = 143, Preview = false },
            new LibraryPackage { Id = "p20", Name = @"ASP.NET Core 10.0.12, разрядность x86", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 10\aspnetcore-runtime-10.0.12-win-x86.exe", Version = @"10.0.12", Architecture = "x86", Kind = LibraryKind.DotNet, Sha256 = "7DAE58DECB46FA51727E6ABE7A7FB5EF6A9A5C623D9263837F64FFB062EDD3BE", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.AspNetCore.App", UninstallKey = @"", Order = 142, Preview = false },
            new LibraryPackage { Id = "p21", Name = @".NET Runtime 10.0.12, разрядность x64", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 10\dotnet-runtime-10.0.12-win-x64.exe", Version = @"10.0.12", Architecture = "x64", Kind = LibraryKind.DotNet, Sha256 = "02EA072C08F890F9DC0D3AC71B5FD4C2BFCDCED4E1133E2D917CE5422B025585", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.NETCore.App", UninstallKey = @"", Order = 141, Preview = false },
            new LibraryPackage { Id = "p22", Name = @".NET Runtime 10.0.12, разрядность x86", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 10\dotnet-runtime-10.0.12-win-x86.exe", Version = @"10.0.12", Architecture = "x86", Kind = LibraryKind.DotNet, Sha256 = "68E16D0F176AEE9C1B78FB1EBA6F1BE7D5E13AA0E7E5AE4DD6465812776A21F0", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.NETCore.App", UninstallKey = @"", Order = 140, Preview = false },
            new LibraryPackage { Id = "p23", Name = @"ASP.NET Core 11.0.0-rc.1.26425.128, разрядность x64", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 11\aspnetcore-runtime-11.0.0-rc.1.26425.128-win-x64.exe", Version = @"11.0.0-rc.1.26425.128", Architecture = "x64", Kind = LibraryKind.DotNet, Sha256 = "5164A836567466B23E35403E371F8994C03E6FABD2125F2EC879BF23E7DFD2A3", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.AspNetCore.App", UninstallKey = @"", Order = 147, Preview = true },
            new LibraryPackage { Id = "p24", Name = @"ASP.NET Core 11.0.0-rc.1.26425.128, разрядность x86", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 11\aspnetcore-runtime-11.0.0-rc.1.26425.128-win-x86.exe", Version = @"11.0.0-rc.1.26425.128", Architecture = "x86", Kind = LibraryKind.DotNet, Sha256 = "7344D7B3BF81F4AB3349083CA6FB6BD940C8910F22A2BF6292F68EEC7237FE62", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.AspNetCore.App", UninstallKey = @"", Order = 146, Preview = true },
            new LibraryPackage { Id = "p25", Name = @".NET Runtime 11.0.0-rc.1.26425.128, разрядность x64", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 11\dotnet-runtime-11.0.0-rc.1.26425.128-win-x64.exe", Version = @"11.0.0-rc.1.26425.128", Architecture = "x64", Kind = LibraryKind.DotNet, Sha256 = "EFC16AB3269050A0321641722A42978BC1BD8FB5A3CD88E48B5DBFCF4BF86CC9", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.NETCore.App", UninstallKey = @"", Order = 145, Preview = true },
            new LibraryPackage { Id = "p26", Name = @".NET Runtime 11.0.0-rc.1.26425.128, разрядность x86", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 11\dotnet-runtime-11.0.0-rc.1.26425.128-win-x86.exe", Version = @"11.0.0-rc.1.26425.128", Architecture = "x86", Kind = LibraryKind.DotNet, Sha256 = "9BE608854BABCC7806BA98E921A44E56B9DDB276B8C230CB5FB812D6A65900D4", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.NETCore.App", UninstallKey = @"", Order = 144, Preview = true },
            new LibraryPackage { Id = "p27", Name = @"ASP.NET Core 6.0.36, разрядность x64", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 6\aspnetcore-runtime-6.0.36-win-x64.exe", Version = @"6.0.36", Architecture = "x64", Kind = LibraryKind.DotNet, Sha256 = "06DBD26509079497C363B28060874D75D19C3797B83020F3C53F37FAA755A61D", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.AspNetCore.App", UninstallKey = @"", Order = 127, Preview = false },
            new LibraryPackage { Id = "p28", Name = @"ASP.NET Core 6.0.36, разрядность x86", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 6\aspnetcore-runtime-6.0.36-win-x86.exe", Version = @"6.0.36", Architecture = "x86", Kind = LibraryKind.DotNet, Sha256 = "AF72A5B08FB14E95C1D05D2542D3DE79D50E31E06CAC181B2A1DF3F87BC1F515", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.AspNetCore.App", UninstallKey = @"", Order = 126, Preview = false },
            new LibraryPackage { Id = "p29", Name = @".NET Runtime 6.0.36, разрядность x64", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 6\dotnet-runtime-6.0.36-win-x64.exe", Version = @"6.0.36", Architecture = "x64", Kind = LibraryKind.DotNet, Sha256 = "6BDAD7BC4C41FE93D4AE7B0312B1D017CFE369D28E7E2E421F5B675F9FEEFE84", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.NETCore.App", UninstallKey = @"", Order = 125, Preview = false },
            new LibraryPackage { Id = "p30", Name = @".NET Runtime 6.0.36, разрядность x86", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 6\dotnet-runtime-6.0.36-win-x86.exe", Version = @"6.0.36", Architecture = "x86", Kind = LibraryKind.DotNet, Sha256 = "3B3CB4636251A582158F4B6B340F20B3861E6793EB9A3E64BDA29CBF32DA3604", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.NETCore.App", UninstallKey = @"", Order = 124, Preview = false },
            new LibraryPackage { Id = "p31", Name = @"ASP.NET Core 8.0.31, разрядность x64", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 8\aspnetcore-runtime-8.0.31-win-x64.exe", Version = @"8.0.31", Architecture = "x64", Kind = LibraryKind.DotNet, Sha256 = "6B9D183E26DF9BD33BD50C2CE5E7BFACB44EB4D65B99553EC431B166D1E10B40", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.AspNetCore.App", UninstallKey = @"", Order = 135, Preview = false },
            new LibraryPackage { Id = "p32", Name = @"ASP.NET Core 8.0.31, разрядность x86", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 8\aspnetcore-runtime-8.0.31-win-x86.exe", Version = @"8.0.31", Architecture = "x86", Kind = LibraryKind.DotNet, Sha256 = "91C2BEFF5BF7109F004CEC1BCE51CE6E6D4D23AF5A6761E24B64501F09329A9F", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.AspNetCore.App", UninstallKey = @"", Order = 134, Preview = false },
            new LibraryPackage { Id = "p33", Name = @".NET Runtime 8.0.31, разрядность x64", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 8\dotnet-runtime-8.0.31-win-x64.exe", Version = @"8.0.31", Architecture = "x64", Kind = LibraryKind.DotNet, Sha256 = "249B0D10D4BFA8EC8DED5E6E220E871E71CCA89290F1A2FDB93CA59ABB8FF97B", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.NETCore.App", UninstallKey = @"", Order = 133, Preview = false },
            new LibraryPackage { Id = "p34", Name = @".NET Runtime 8.0.31, разрядность x86", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 8\dotnet-runtime-8.0.31-win-x86.exe", Version = @"8.0.31", Architecture = "x86", Kind = LibraryKind.DotNet, Sha256 = "2C1691932F72D166DC4D5A9CF6ACCF94D94566FEECD3CFED205ED85BAEACF44C", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.NETCore.App", UninstallKey = @"", Order = 132, Preview = false },
            new LibraryPackage { Id = "p35", Name = @"ASP.NET Core 9.0.20, разрядность x64", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 9\aspnetcore-runtime-9.0.20-win-x64.exe", Version = @"9.0.20", Architecture = "x64", Kind = LibraryKind.DotNet, Sha256 = "959D7AFD6B71B10799872A0EDA558A132B2DC6D60B62E79A0742B94404C26E3D", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.AspNetCore.App", UninstallKey = @"", Order = 139, Preview = false },
            new LibraryPackage { Id = "p36", Name = @"ASP.NET Core 9.0.20, разрядность x86", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 9\aspnetcore-runtime-9.0.20-win-x86.exe", Version = @"9.0.20", Architecture = "x86", Kind = LibraryKind.DotNet, Sha256 = "A49246108F0D3889CB3B18657A9482936F4F379E6E23075669D2641B5F2A795E", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.AspNetCore.App", UninstallKey = @"", Order = 138, Preview = false },
            new LibraryPackage { Id = "p37", Name = @".NET Runtime 9.0.20, разрядность x64", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 9\dotnet-runtime-9.0.20-win-x64.exe", Version = @"9.0.20", Architecture = "x64", Kind = LibraryKind.DotNet, Sha256 = "6ECB0EB560DDB25C9318D28CEFBA06C752F395BB9BE03EF78F6F4DBFF58F3249", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.NETCore.App", UninstallKey = @"", Order = 137, Preview = false },
            new LibraryPackage { Id = "p38", Name = @".NET Runtime 9.0.20, разрядность x86", Group = @"Microsoft .NET", RelativePath = @"Microsoft .NET\.NET 9\dotnet-runtime-9.0.20-win-x86.exe", Version = @"9.0.20", Architecture = "x86", Kind = LibraryKind.DotNet, Sha256 = "413A1EABC23A3CF772D51500CAEB97DED1A0352891C8DE793E7996E7A2EE8A95", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"Microsoft.NETCore.App", UninstallKey = @"", Order = 136, Preview = false },
            new LibraryPackage { Id = "p39", Name = @"Visual C++ 2005–2022, разрядность x86/x64", Group = @"Visual C++ и RuntimePack", RelativePath = @"0.Microsoft Visual C++ 2005-2022 Redistributable 14.52.37025.0.exe", Version = @"14.52.37025.0", Architecture = "both", Kind = LibraryKind.VcBundle, Sha256 = "4E7483652C94AA470B4D943975E2D75F4D3457D8ECCC502F0135EDCF66A81220", ProductCode = @"", UpgradeCode = @"", RuntimeFamily = @"", UninstallKey = @"", Order = 400, Preview = false },
        };
        private static List<LibraryPackage> current = Normalize(Entries.Select(p => { p.Id = LibraryDiscovery.Id(p); return p; }));
        private static readonly object Gate = new object();
        private static List<LibraryPackage> Normalize(IEnumerable<LibraryPackage> packages)
        {
            var values = packages.Where(p => p != null && IsLibraryLocation(p.RelativePath) && p.Kind != LibraryKind.Composite && (p.Name ?? "").IndexOf("RuntimePack Full", StringComparison.OrdinalIgnoreCase) < 0 && (p.RelativePath ?? "").IndexOf("RuntimePack_Full", StringComparison.OrdinalIgnoreCase) < 0).ToList();
            foreach (var package in values.Where(p => p.Kind == LibraryKind.VcBundle || p.Kind == LibraryKind.VcLegacy))
            {
                package.Group = "Visual C++"; package.Name = package.Kind == LibraryKind.VcLegacy ? "Visual C++ 2012/2013: профиль 2005–2013 + Lite" : "Visual C++ v14 (2015–2026) + Lite";
            }
            foreach (var modern in values.Where(p => p.Kind == LibraryKind.VcBundle).ToList())
                if (!values.Any(p => p.Kind == LibraryKind.VcLegacy)) values.Add(LibraryDiscovery.Legacy(modern));
            return values;
        }
        private static bool IsLibraryLocation(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return false;
            string[] parts = path.Split('\\', '/');
            return (parts.Length == 1 || String.Equals(parts[0], "Microsoft .NET", StringComparison.OrdinalIgnoreCase))
                && SoftwareCatalog.PackageId(parts[parts.Length - 1]) == null;
        }

        internal static string CachePath { get { return ApplicationStorage.FilePath("library-catalog.xml"); } }
        public static List<LibraryPackage> All() { lock (Gate) return current.OrderBy(p => p.Order).ToList(); }
        public static LibraryPackage Find(string id) { lock (Gate) return current.FirstOrDefault(p => p.Id == id); }
        public static void Replace(IEnumerable<LibraryPackage> packages)
        {
            var values = Normalize(packages);
            if (values.Count == 0 || values.Count > 256 || values.Select(p => p.Id).Distinct().Count() != values.Count) throw new InvalidDataException("Некорректный каталог пакетов.");
            foreach (LibraryPackage value in values) Validate(value);
            lock (Gate) current = values;
        }
        public static void Merge(IEnumerable<LibraryPackage> packages)
        {
            var all = All().ToDictionary(p => p.Id);
            foreach (LibraryPackage package in packages) all[package.Id] = package;
            Replace(all.Values);
        }
        public static void Validate(LibraryPackage p)
        {
            if (p == null || !System.Text.RegularExpressions.Regex.IsMatch(p.Id ?? "", "^[a-zA-Z0-9_-]{1,100}$") || !Enum.IsDefined(typeof(LibraryKind), p.Kind) ||
                String.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 512 || String.IsNullOrWhiteSpace(p.RelativePath) || p.RelativePath.Length > 1024 ||
                Path.IsPathRooted(p.RelativePath) || p.RelativePath.IndexOf(':') >= 0 || p.RelativePath.IndexOf('\0') >= 0 ||
                !System.Text.RegularExpressions.Regex.IsMatch(p.Sha256 ?? "", "^[0-9A-Fa-f]{64}$")) throw new InvalidDataException("Некорректная запись каталога.");
            if (p.Kind == LibraryKind.Msi) { Guid code; if (!Guid.TryParse(p.ProductCode, out code)) throw new InvalidDataException("Некорректный MSI ProductCode."); }
        }
        private static XmlElement Element(XmlDocument doc, LibraryPackage p)
        {
            Validate(p); XmlElement e = doc.CreateElement("package");
            foreach (System.Reflection.FieldInfo field in typeof(LibraryPackage).GetFields())
                e.SetAttribute(field.Name, Convert.ToString(field.GetValue(p), System.Globalization.CultureInfo.InvariantCulture));
            return e;
        }
        public static string Serialize(LibraryPackage p) { var doc = new XmlDocument(); doc.AppendChild(Element(doc, p)); return doc.OuterXml; }
        public static LibraryPackage Deserialize(string xml)
        {
            if (xml == null || xml.Length > 12000) throw new InvalidDataException("Слишком большая запись каталога.");
            var doc = LoadXml(new StringReader(xml)); return Read(doc.DocumentElement);
        }
        private static LibraryPackage Read(XmlElement e)
        {
            if (e == null || e.Name != "package") throw new InvalidDataException("Неверный формат пакета.");
            var p = new LibraryPackage();
            foreach (System.Reflection.FieldInfo field in typeof(LibraryPackage).GetFields())
            {
                if (!e.HasAttribute(field.Name)) continue;
                string value = e.GetAttribute(field.Name);
                if (field.FieldType == typeof(string)) field.SetValue(p, value);
                else if (field.FieldType == typeof(bool)) field.SetValue(p, Boolean.Parse(value));
                else if (field.FieldType == typeof(int)) field.SetValue(p, Int32.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                else if (field.FieldType.IsEnum) field.SetValue(p, Enum.Parse(field.FieldType, value, false));
            }
            Validate(p); return p;
        }
        private static XmlDocument LoadXml(TextReader text)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2097152 };
            using (XmlReader reader = XmlReader.Create(text, settings)) { var doc = new XmlDocument { XmlResolver = null }; doc.Load(reader); return doc; }
        }
        public static void Save(string path)
        {
            var doc = new XmlDocument(); XmlElement root = doc.CreateElement("libraries"); root.SetAttribute("schema", "1"); doc.AppendChild(root);
            foreach (LibraryPackage p in All()) root.AppendChild(Element(doc, p));
            string directory = Path.GetDirectoryName(Path.GetFullPath(path)); Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { doc.Save(temporary); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static List<LibraryPackage> ReadFile(string path)
        {
            using (var text = new StreamReader(path, Encoding.UTF8, true))
            {
                XmlDocument doc = LoadXml(text);
                if (doc.DocumentElement == null || doc.DocumentElement.Name != "libraries") throw new InvalidDataException("Неверный формат каталога.");
                return doc.DocumentElement.ChildNodes.OfType<XmlElement>().Select(Read).ToList();
            }
        }
        public static void Load(string path) { Replace(ReadFile(path)); }
    }
}
