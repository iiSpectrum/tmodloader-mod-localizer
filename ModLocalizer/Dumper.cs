﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using ModLocalizer.Extensions;
using ModLocalizer.Framework;
using ModLocalizer.ModLoader;
using Newtonsoft.Json;

namespace ModLocalizer
{
    internal sealed class Dumper
    {
        private readonly TmodFile _mod;

        private readonly byte[] _assembly;

        private ModuleDef _module;

        public Dumper(TmodFile mod)
        {
            _mod = mod;

            _assembly = _mod.GetPrimaryAssembly(false);
        }

        public void Run()
        {
            try
            {
                Directory.Delete(_mod.Name, true);
            }
            catch
            {
                // ignored
            }

            Directory.CreateDirectory(_mod.Name);
            Directory.CreateDirectory(GetPath(DefaultConfigurations.LocalizerFiles.ItemFolder));
            Directory.CreateDirectory(GetPath(DefaultConfigurations.LocalizerFiles.NpcFolder));
            Directory.CreateDirectory(GetPath(DefaultConfigurations.LocalizerFiles.BuffFolder));
            Directory.CreateDirectory(GetPath(DefaultConfigurations.LocalizerFiles.MiscFolder));
            Directory.CreateDirectory(GetPath(DefaultConfigurations.LocalizerFiles.TileFolder));
            Directory.CreateDirectory(GetPath(DefaultConfigurations.LocalizerFiles.CustomFolder));

            LoadAssembly();
            DumpBuildProperties();
            DumpTmodProperties();
            DumpItems();
            DumpNpcs();
            DumpBuffs();
            DumpMiscs();
            DumpMapEntries();
            DumpCustomTranslations();

            DumpResources();
        }

        private void LoadAssembly()
        {
#if DEBUG
            File.WriteAllBytes("dump_temp_mod.dll", _assembly);
#endif

            var assembly = AssemblyDef.Load(_assembly);

            _module = assembly.Modules.Single();
        }

        private void DumpBuildProperties()
        {
            var infoData = _mod.GetFile(TmodFile.InfoFileName);

            var properties = BuildProperties.ReadBytes(infoData);

            using (var fs = new FileStream(GetPath(DefaultConfigurations.LocalizerFiles.InfoConfigurationFile), FileMode.Create))
            {
                using (var sw = new StreamWriter(fs))
                {
                    sw.Write(JsonConvert.SerializeObject(properties, Formatting.Indented));
                }
            }
        }

        private void DumpTmodProperties()
        {
            var properties = _mod.Properties;

            using (var fs = new FileStream(GetPath(DefaultConfigurations.LocalizerFiles.ModInfoConfigurationFile), FileMode.Create))
            {
                using (var sw = new StreamWriter(fs))
                {
                    sw.Write(JsonConvert.SerializeObject(properties, Formatting.Indented));
                }
            }
        }

        private void DumpItems()
        {
            var items = new List<ItemTranslation>();

            foreach (var type in _module.Types.Where(
                t => t.HasBaseType("Terraria.ModLoader.ModItem")))
            {
                var item = new ItemTranslation { TypeName = type.Name, Namespace = type.Namespace };

                var method = type.FindMethod("SetStaticDefaults", MethodSig.CreateInstance(_module.CorLibTypes.Void));
                if (method?.HasBody == true)
                {
                    var inst = method.Body.Instructions;

                    for (var index = 0; index < inst.Count; index++)
                    {
                        var ins = inst[index];

                        if (ins.OpCode != OpCodes.Ldstr)
                            continue;

                        var value = ins.Operand as string;

                        ins = inst[++index];

                        if (ins.Operand is IMethodDefOrRef m &&
                            string.Equals(m.Name.ToString(), "SetDefault") &&
                            string.Equals(m.DeclaringType.Name, "ModTranslation", StringComparison.Ordinal))
                        {
                            ins = inst[index - 2];

                            if (!(ins?.Operand is IMethodDefOrRef propertyGetter))
                            {
                                // some translation objects may get from stack;
                                // In this case, we can't know their type. skip
                                continue;
                            }

                            switch (propertyGetter.Name)
                            {
                                case "get_Tooltip":
                                    item.ToolTip = value;
                                    break;
                                case "get_DisplayName":
                                    item.Name = value;
                                    break;
                            }
                        }
                    }
                }

                method = type.FindMethod("ModifyTooltips");
                if (method?.HasBody == true)
                {
                    var inst = method.Body.Instructions;

                    for (var index = 0; index < inst.Count; index++)
                    {
                        var ins = inst[index];

                        if (ins.OpCode != OpCodes.Newobj || !(ins.Operand is MemberRef m) || !m.DeclaringType.Name.Equals("TooltipLine"))
                            continue;

                        ins = inst[index - 1];

                        if (ins.OpCode.Equals(OpCodes.Ldstr) && inst[index - 2].OpCode.Equals(OpCodes.Ldstr))
                        {
                            item.ModifyTooltips.Add(inst[index - 2].Operand as string);
                            item.ModifyTooltips.Add(inst[index - 1].Operand as string);
                        }
                        else if (ins.OpCode.Equals(OpCodes.Call) && ins.Operand is MemberRef n && n.Name.Equals("Concat"))
                        {
                            var index2 = index;
                            var count = 0;
                            var total = n.MethodSig.Params.Count + 1;
                            var list = new List<string>();
                            while (--index2 > 0 && count < total)
                            {
                                ins = inst[index2];
                                if (ins.OpCode.Equals(OpCodes.Ldelem_Ref))
                                {
                                    count++;
                                }
                                else if (ins.OpCode.Equals(OpCodes.Ldstr))
                                {
                                    count++;
                                    list.Add(ins.Operand as string);
                                }
                            }
                            list.Reverse();
                            item.ModifyTooltips.AddRange(list);
                        }
                    }
                }

                method = type.FindMethod("UpdateArmorSet");
                if (method?.HasBody == true)
                {
                    var inst = method.Body.Instructions;

                    for (var index = 0; index < inst.Count; index++)
                    {
                        var ins = inst[index];

                        if (ins.OpCode != OpCodes.Ldstr)
                            continue;

                        var value = ins.Operand as string;

                        if ((ins = inst[++index]).OpCode == OpCodes.Stfld && ins.Operand is MemberRef m)
                        {
                            switch (m.Name)
                            {
                                case "setBonus":
                                    item.SetBonus = value;
                                    break;
                            }
                        }
                    }
                }

                items.Add(item);
            }

            WriteFiles(items, DefaultConfigurations.LocalizerFiles.ItemFolder);
        }

        private void DumpNpcs()
        {
            var npcs = new List<NpcTranslation>();

            foreach (var type in _module.Types.Where(
                t => t.HasBaseType("Terraria.ModLoader.ModNPC")))
            {
                var npc = new NpcTranslation { TypeName = type.Name, Namespace = type.Namespace };

                var method = type.FindMethod("SetStaticDefaults", MethodSig.CreateInstance(_module.CorLibTypes.Void));
                if (method?.HasBody == true)
                {
                    var inst = method.Body.Instructions;

                    for (var index = 0; index < inst.Count; index++)
                    {
                        var ins = inst[index];

                        if (ins.OpCode != OpCodes.Ldstr)
                            continue;

                        var value = ins.Operand as string;

                        ins = inst[++index];

                        if (ins.Operand is IMethodDefOrRef m &&
                            string.Equals(m.Name.ToString(), "SetDefault") &&
                            string.Equals(m.DeclaringType.Name, "ModTranslation", StringComparison.Ordinal))
                        {
                            ins = inst[index - 2];

                            if (!(ins?.Operand is IMethodDefOrRef propertyGetter))
                            {
                                // some translation objects may get from stack;
                                // In this case, we can't know their type. skip
                                continue;
                            }

                            switch (propertyGetter.Name)
                            {
                                case "get_DisplayName":
                                    npc.Name = value;
                                    break;
                            }
                        }
                    }
                }

                method = type.FindMethod("GetChat");
                if (method?.HasBody == true)
                {
                    var inst = method.Body.Instructions;

                    foreach (var ins in inst)
                    {
                        if (ins.OpCode != OpCodes.Ldstr)
                            continue;

                        var value = ins.Operand as string;

                        npc.ChatTexts.Add(value);
                    }
                }

                method = type.FindMethod("SetChatButtons");
                if (method?.HasBody == true)
                {
                    var inst = method.Body.Instructions;

                    for (var index = 0; index < inst.Count; index++)
                    {
                        var ins = inst[index];

                        if (ins.OpCode != OpCodes.Ldstr)
                            continue;

                        var value = ins.Operand as string;

                        // try find which button is here
                        // however, not every mod follows this rule

                        ins = inst.ElementAtOrDefault(index - 1);
                        if (ins == null)
                            continue;

                        if (ins.OpCode.Equals(OpCodes.Ldarg_1))
                            npc.ShopButton1 = value;
                        else if (ins.OpCode.Equals(OpCodes.Ldarg_2))
                            npc.ShopButton2 = value;
                    }
                }

                method = type.FindMethod("TownNPCName");
                if (method?.HasBody == true)
                {
                    var inst = method.Body.Instructions;

                    foreach (var ins in inst)
                    {
                        if (ins.OpCode != OpCodes.Ldstr)
                            continue;

                        var value = ins.Operand as string;

                        npc.TownNpcNames.Add(value);
                    }
                }

                npcs.Add(npc);
            }

            WriteFiles(npcs, DefaultConfigurations.LocalizerFiles.NpcFolder);
        }

        private void DumpBuffs()
        {
            var buffs = new List<BuffTranslation>();

            foreach (var type in _module.Types.Where(t => t.HasBaseType("Terraria.ModLoader.ModBuff")))
            {
                var buff = new BuffTranslation { TypeName = type.Name, Namespace = type.Namespace };

                var method = type.FindMethod("SetDefaults", MethodSig.CreateInstance(_module.CorLibTypes.Void));

                if (method?.HasBody != true)
                    continue;

                var inst = method.Body.Instructions;

                for (var index = 0; index < inst.Count; index++)
                {
                    var ins = inst[index];

                    if (ins.OpCode != OpCodes.Ldstr)
                        continue;

                    var value = ins.Operand as string;

                    ins = inst[++index];

                    if (ins.Operand is IMethodDefOrRef m &&
                        string.Equals(m.Name.ToString(), "SetDefault") &&
                        string.Equals(m.DeclaringType.Name, "ModTranslation", StringComparison.Ordinal))
                    {
                        ins = inst[index - 2];

                        var propertyGetter = (IMethodDefOrRef)ins.Operand;
                        switch (propertyGetter.Name)
                        {
                            case "get_DisplayName":
                                buff.Name = value;
                                break;
                            case "get_Description":
                                buff.Tip = value;
                                break;
                        }
                    }
                }

                buffs.Add(buff);
            }

            WriteFiles(buffs, DefaultConfigurations.LocalizerFiles.BuffFolder);
        }

        private void DumpMiscs()
        {
            var miscs = new List<NewTextTranslation>();

            foreach (var type in _module.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    var inst = method.Body.Instructions;

                    var misc = new NewTextTranslation { TypeName = type.Name, Namespace = type.Namespace, Method = method.Name };
                    var write = false;

                    for (var index = 0; index < inst.Count; index++)
                    {
                        var ins = inst[index];

                        if (!ins.OpCode.Equals(OpCodes.Call) || !(ins.Operand is IMethodDefOrRef m) ||
                            !m.Name.ToString().Equals("NewText", StringComparison.Ordinal))
                            continue;

                        if ((ins = inst[index - 5]).OpCode.Equals(OpCodes.Ldstr))
                        {
                            misc.Contents.Add(ins.Operand as string);
                            write = true;
                        }
                    }

                    if (write)
                        miscs.Add(misc);
                }
            }

            WriteFiles(miscs, DefaultConfigurations.LocalizerFiles.MiscFolder);
        }

        private void DumpMapEntries()
        {
            var entries = new List<MapEntryTranslation>();

            foreach (var type in _module.Types.Where(t => t.HasBaseType("Terraria.ModLoader.ModTile")))
            {
                var entry = new MapEntryTranslation { TypeName = type.Name, Namespace = type.Namespace };

                var method = type.FindMethod("SetDefaults", MethodSig.CreateInstance(_module.CorLibTypes.Void));

                if (method?.HasBody != true)
                    continue;

                var inst = method.Body.Instructions;

                for (var index = 0; index < inst.Count; index++)
                {
                    var ins = inst[index];

                    if (ins.OpCode != OpCodes.Ldstr)
                        continue;

                    var value = ins.Operand as string;

                    ins = inst[++index];

                    if (ins.Operand is IMethodDefOrRef m &&
                        string.Equals(m.Name.ToString(), "SetDefault") &&
                        string.Equals(m.DeclaringType.Name, "ModTranslation", StringComparison.Ordinal))
                    {
                        entry.Name = value;
                    }
                }

                entries.Add(entry);
            }

            WriteFiles(entries, "Tiles");
        }

        private void DumpCustomTranslations()
        {
            var list = new List<CustomTranslation>();

            foreach (var type in _module.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    var inst = method.Body.Instructions;
                    for (var index = 0; index < inst.Count; index++)
                    {
                        var ins = inst[index];

                        if (!ins.OpCode.Equals(OpCodes.Call) ||
                            !(ins.Operand is IMethod m) ||
                            !string.Equals(m.Name.ToString(), "CreateTranslation"))
                            continue;

                        if (!(ins = inst[++index]).IsStloc())
                            continue;

                        var custom = new CustomTranslation
                        {
                            Key = inst[index - 2].Operand as string,
                            Namespace = type.Namespace
                        };

                        var local = ins.GetLocal(method.Body.Variables);

                        while (index < inst.Count - 1 && !(ins = inst[++index]).IsLdloc() || ins.GetLocal(method.Body.Variables) != local) { }

                        if (!(ins = inst[++index]).OpCode.Equals(OpCodes.Ldstr))
                            continue;

                        custom.Value = ins.Operand as string;
                        list.Add(custom);
                    }
                }
            }

            WriteFiles(list, "Customs");
        }

        private void DumpResources()
        {
            foreach (var resourceFile in _mod.GetResourceFiles())
            {
                var path = resourceFile.Replace(TmodFile.PathSeparator, Path.DirectorySeparatorChar);
                var directoryName = Path.GetDirectoryName(path);

                if (directoryName != null)
                {
                    Directory.CreateDirectory(GetPath(directoryName));
                }

                File.WriteAllBytes(GetPath(path), _mod.GetFile(resourceFile));
            }
        }

        private string GetPath(params string[] paths) => Path.Combine(_mod.Name, Path.Combine(paths));

        public void WriteFiles<T>(IList<T> translations, string category) where T : ITranslation
        {
            foreach (var ns in translations.Select(i => i.Namespace).Distinct())
            {
                using (var fs = File.Create(GetPath(category, ns + ".json")))
                {
                    using (var sr = new StreamWriter(fs))
                    {
                        sr.Write(JsonConvert.SerializeObject(translations.Where(i => i.Namespace.Equals(ns)).ToList(), Formatting.Indented));
                    }
                }
            }
        }
    }
}