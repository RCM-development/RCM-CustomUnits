using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Shapes2D;
using TestMod;
using UnityEngine;
using UnityEngine.UI;

namespace RCM_CustomUnits{


    [BepInDependency(RCMManager.IDENTIFIER, BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(IDENTIFIER, "Custom Units Plugin", "1.0.0.0")]
    internal class CustomUnits : BaseUnityPlugin
    {
        const string IDENTIFIER = "RCM.plugins.customunits";
        private void Awake(){
            new Harmony(IDENTIFIER).PatchAll();
            RCMManager.ConnectMod("Custom Unit Loader").ContinueWith(t => {
                RCMModUI mod = t.Result;

                // begin mod UI construction here...
                mod.CreateLabelField("patches applied.");


            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        // load custom unit patch code stuff

        public static List<KeyValuePair<string, bool>> entities_to_localize = new List<KeyValuePair<string, bool>>();
        public static List<EntityBalancingParameters> entities_to_append = new List<EntityBalancingParameters>();
        public static Dictionary<string, AssetBundle> mod_bundles = new Dictionary<string, AssetBundle>();

        static bool has_loaded = false;
        static void VerifyCustomUnitsLoaded()
        {
            if (has_loaded) return; has_loaded = true;
            // copied from the actual function, because we should always block if true
            //if (EntityBalancingStore.EntityBalancingParametersList != null)
            //    return;

            RCMManager.Log("hook reached!!");
            // locate folder with all asset bundles
            List<AssetBundle> loaded_bundles = new List<AssetBundle>();
            string assets_folder = "C:\\Users\\ct770\\DIRECTORY\\PROJECTS\\Rogue Command\\DUMP\\custom1";
            foreach (string file in Directory.GetFiles(assets_folder))
            {
                try
                {
                    AssetBundle mod_bundle = AssetBundle.LoadFromFile(file);
                    if (mod_bundle != null) loaded_bundles.Add(mod_bundle);
                }
                catch (Exception ex)
                {
                    RCMManager.Log("bundle opening exception: " + ex);
                }
            }
            // foreach asset bundle
            foreach (AssetBundle bundle in loaded_bundles)
            {
                try
                {
                    // open the 'units' scriptable object, append structures to entity list
                    EntityBalancingScriptableObject bundle_units = (EntityBalancingScriptableObject)bundle.LoadAsset("Units");
                    if (bundle_units == null)
                    {
                        RCMManager.Log("bundle unit processing exception: entity balancing obj is null");
                        continue;
                    }

                    entities_to_append.AddRange(bundle_units.parameters);

                    foreach (EntityBalancingParameters curr_entity in bundle_units.parameters)
                    {
                        // open localisation files, and process them via the same method as the game
                        //TextAsset blueprint_name = (TextAsset)bundle.LoadAsset("Localization - BlueprintName");
                        //TextAsset blueprint_desc = (TextAsset)bundle.LoadAsset("Localization - BlueprintDescription");
                        //TextAsset skill_desc = (TextAsset)bundle.LoadAsset("Localization - SkillDescription");

                        // since the localizations haven't actually loaded yet, just set aside the info for later
                        entities_to_localize.Add(new KeyValuePair<string, bool>(curr_entity.entityId, curr_entity.skillType != SkillType.None && ((curr_entity.roles & UnitRole.Factory) == 0)));

                        RCMManager.Log("bundle unit processing success: " + curr_entity.entityId);
                    }
                    mod_bundles.Add(bundle.name, bundle);

                    // temp thing to prevent entityid overlap
                    //foreach (var item in bundle_units.parameters)
                    //{
                    //    for (int i = 0; i < entity.parameters.Count; i++)
                    //    {
                    //        if (entity.parameters[i].entityId == item.entityId)
                    //        {
                    //            entity.parameters.RemoveAt(i);
                    //            entity.parameters.Add(item);
                    //            RCMManager.Log("replaced entity: " + item.entityId);
                    //        }
                    //    }
                    //}


                }
                catch (Exception ex)
                {
                    RCMManager.Log("bundle unit processing exception: " + ex);
                }
            }
        }

        static bool has_loaded_entities = false;
        [HarmonyPatch(typeof(EntityBalancingStore), "Init", new Type[] { })]
        private static class UnitPatch{
            public static void Postfix(){
                RCMManager.Log("init'ing entities");
                if (has_loaded_entities) return; has_loaded_entities = true;
                VerifyCustomUnitsLoaded();
                foreach (var v in entities_to_append){
                    EntityBalancingStore.ParameterListIndexOf.Add(v.entityId, EntityBalancingStore.EntityBalancingParametersList.Count);
                    EntityBalancingStore.EntityBalancingParametersList.Add(v);

                    EntityBalancingStore.ChangeableIntValueCache.Add(v.entityId, new Dictionary<EntityBalancingStore.ChangeableValue, int>());
                    EntityBalancingStore.ChangeableFloatValueCache.Add(v.entityId, new Dictionary<EntityBalancingStore.ChangeableValue, float>());

                    if (v.factoryForEntityId.hasValue) EntityBalancingStore.FactoryEntityIdOf[v.factoryForEntityId.value] = v.entityId;
                }
            }
        }
        [HarmonyPatch(typeof(Loca), "Init")]
        private static class LocalizationPatch{
            private static void Postfix(){
                RCMManager.Log("updating localizations");
                VerifyCustomUnitsLoaded();
                foreach (var kvp in entities_to_localize){
                    string item = kvp.Key;
                    bool has_skill = kvp.Value;
                    string entry_key = item.Trim().ToLower();
                    Loca.BlueprintNameDictionary["en-US"][entry_key] = "placeholder" + item;
                    Loca.BlueprintDescriptionDictionary["en-US"][entry_key] = "placeholder" + item;

                    if (has_skill) Loca.SkillDescriptionDictionary["en-US"][item] = item + "'s custom skill";

                    RCMManager.Log("bundle unit localized: " + entry_key);
                }

            }
        }


        // this function overrides: Resources.Load(string, Type)
        [HarmonyPatch]
        public static class Resources_Load_String_Type_Patch{
            [HarmonyTargetMethod]
            public static MethodBase TargetMethod(){
                return typeof(Resources).GetMethod(
                    "Load",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(string), typeof(Type) },
                    null
                );
            }
            [HarmonyPrefix]
            public static bool Prefix(ref UnityEngine.Object __result, string path, Type systemTypeInstance){
                if (path.Length > 0 && path[0] == '<'){
                    string[] strings = path.Split(new char[] { '>' });

                    string asset_bundle = strings[0].Substring(1);
                    string asset_path = strings[1];

                    if (mod_bundles.TryGetValue(asset_bundle, out AssetBundle bundle)){
                        RCMManager.Log("successfully loadaed: " + asset_path + " from bundle: " + asset_bundle);
                        __result = bundle.LoadAsset(asset_path, systemTypeInstance);
                        return false; // skip original
                    } else{
                        RCMManager.Log("CRITICAL FAILURE!!!! bundle not found: " + asset_bundle);
                        __result = null;
                        return false; // skip original
                    }
                }
                // load resource regularly
                return true;
            }
        }
    }
}
