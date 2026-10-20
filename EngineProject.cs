using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Input;

namespace EngineSoundTool;

public sealed record EngineModDetails(string Name,string Author,string Version);
public sealed record EngineProjectInfo(int FormatVersion,string Kind,string GamePath,EngineModDetails Mod,int[] Replacements);
public sealed record OpenedEngineProject(BdlDocument Document,EngineProjectInfo Info);

public static class EngineProject
{
    public const string Extension=".egoengineaudio";
    public static string? FindGamePath(string path)
    {
        path=Path.GetFullPath(path);
        for(var dir=new DirectoryInfo(Path.GetDirectoryName(path)!);dir!=null;dir=dir.Parent)
            if(dir.Name.Equals("audio",StringComparison.OrdinalIgnoreCase))return "audio/"+Path.GetRelativePath(dir.FullName,path).Replace('\\','/');
        return null;
    }
    public static string ValidatePath(string path)
    {
        var parts=path.Replace('\\','/').Split('/');
        if(parts.Length<3 || parts[0]!="audio" || parts.Any(p=>string.IsNullOrWhiteSpace(p)||p is "." or ".."||p.IndexOfAny(Path.GetInvalidFileNameChars())>=0) || !parts[^1].EndsWith(".bdl",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Please choose a BDL destination path inside audio.");
        return string.Join('/',parts);
    }
    public static void Save(BdlDocument document,string output,string gamePath,EngineModDetails details)
    {
        gamePath=ValidatePath(gamePath);
        if(!Path.GetFileName(gamePath).Equals(Path.GetFileName(document.SourcePath),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The original BDL filename must be preserved.");
        using var buffer=new MemoryStream();
        using(var archive=new ZipArchive(buffer,ZipArchiveMode.Create,true))
        {
        void Write(string name,byte[] data){using var stream=archive.CreateEntry(name,CompressionLevel.Optimal).Open();stream.Write(data);}
        var changes=document.Sections.Where(s=>document.IsReplaced(s.Index)).Select(s=>s.Index).ToArray();
        Write("original.bdl",document.GetOriginal());
        foreach(int index in changes)Write($"replacements/{index}.wav",PcmWave.Encode(document.GetPcm(index)));
        Write("project.json",JsonSerializer.SerializeToUtf8Bytes(new EngineProjectInfo(1,"ego-engine-audio",gamePath,details,changes),new JsonSerializerOptions{WriteIndented=true}));
        }
        using var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write);
        buffer.Position=0;buffer.CopyTo(file);
    }
    public static OpenedEngineProject Open(string path)
    {
        using var archive=ZipFile.OpenRead(path);
        byte[] Read(string name)
        {
            var entries=archive.Entries.Where(e=>e.FullName==name).ToArray();
            if(entries.Length!=1)throw new InvalidDataException("Missing or duplicate project file: "+name);
            using var input=entries[0].Open();using var output=new MemoryStream();input.CopyTo(output);return output.ToArray();
        }
        var info=JsonSerializer.Deserialize<EngineProjectInfo>(Read("project.json"))??throw new InvalidDataException("Invalid engine audio project.");
        if(info.FormatVersion!=1 || info.Kind!="ego-engine-audio" || info.Mod==null || info.Replacements==null || info.Replacements.Distinct().Count()!=info.Replacements.Length)throw new InvalidDataException("Unsupported engine audio project.");
        string gamePath=ValidatePath(info.GamePath);
        var doc=new BdlDocument(gamePath,Read("original.bdl"));
        foreach(int index in info.Replacements)
        {
            if(index<0 || index>=doc.Sections.Count)throw new InvalidDataException("Invalid project section.");
            doc.Replace(index,Read($"replacements/{index}.wav"));
        }
        return new(doc,info);
    }
    public static void ExportMod(BdlDocument document,string output,string gamePath,EngineModDetails details)
    {
        gamePath=ValidatePath(gamePath);output=Path.GetFullPath(output);
        string projectOutput=Path.ChangeExtension(output,Extension);
        if(File.Exists(output)||File.Exists(projectOutput))throw new IOException("The mod or project already exists. Please choose a new name.");
        string staging=Path.Combine(Path.GetTempPath(),"EgoEngineMod",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        try
        {
            string mod=Path.Combine(staging,"mod.zip"),project=Path.Combine(staging,"project"+Extension);
            Save(document,project,gamePath,details);
            using(var archive=new ZipArchive(File.Create(mod),ZipArchiveMode.Create))
            {
                using(var data=archive.CreateEntry("F1 25/"+gamePath,CompressionLevel.Optimal).Open())data.Write(document.Build());
                using var manifest=archive.CreateEntry("mod.serps").Open();
                JsonSerializer.Serialize(manifest,new{mod_name=details.Name,author=details.Author,version=details.Version,game="F1 25"},new JsonSerializerOptions{WriteIndented=true});
            }
            File.Copy(mod,output,false);
            try{File.Copy(project,projectOutput,false);}catch{File.Delete(output);throw;}
        }
        finally{Directory.Delete(staging,true);}
    }

}
