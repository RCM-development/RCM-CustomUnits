using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using TestMod;
using BepInEx;
using BepInEx.Bootstrap;
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

        public static List<string> entities_to_localize = new List<string>();
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
                        // we may need resource re-routing for this (where we hijack the resource loading to accept asset bundles)

                        // since the localizations haven't actually loaded yet, just set aside the info for later
                        entities_to_localize.Add(curr_entity.entityId);

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
        private static class UnitPatch
        {
            public static void Postfix()
            {
                RCMManager.Log("init'ing entities");
                if (has_loaded_entities) return; has_loaded_entities = true;
                VerifyCustomUnitsLoaded();
                foreach (var v in entities_to_append)
                {
                    EntityBalancingStore.ParameterListIndexOf.Add(v.entityId, EntityBalancingStore.EntityBalancingParametersList.Count);
                    EntityBalancingStore.EntityBalancingParametersList.Add(v);

                    EntityBalancingStore.ChangeableIntValueCache.Add(v.entityId, new Dictionary<EntityBalancingStore.ChangeableValue, int>());
                    EntityBalancingStore.ChangeableFloatValueCache.Add(v.entityId, new Dictionary<EntityBalancingStore.ChangeableValue, float>());


                    if (v.factoryForEntityId.hasValue)
                    {
                        //EntityBalancingStore.FactoryEntityIdOf[v.entityId] = v.factoryForEntityId.value;
                        EntityBalancingStore.FactoryEntityIdOf[v.factoryForEntityId.value] = v.entityId;
                    }
                }

            }

        }


        [HarmonyPatch(typeof(Loca), "Init")]
        private static class LocalizationPatch
        {
            private static void Postfix()
            {
                RCMManager.Log("updating localizations");
                VerifyCustomUnitsLoaded();
                foreach (string item in entities_to_localize)
                {
                    string entry_key = item.Trim().ToLower();
                    Loca.BlueprintNameDictionary["en-US"][entry_key] = "placeholder" + item;
                    Loca.BlueprintDescriptionDictionary["en-US"][entry_key] = "placeholder" + item;
                    //Loca.SkillDescriptionDictionary["en-US"][item] = item;

                    RCMManager.Log("bundle unit localized: " + entry_key);
                }

            }
        }


        public static UnityEngine.Object load_from_bundle_or_resource(string path)
        {
            if (path.Length > 0 && path[0] == '<')
            {
                string[] strings = path.Split(new char[] { '>' });

                string asset_bundle = strings[0].Substring(1);
                string asset_path = strings[1];

                if (mod_bundles.TryGetValue(asset_bundle, out AssetBundle bundle))
                {
                    RCMManager.Log("successfully loadaed: " + asset_path + " from bundle: " + asset_bundle);
                    return bundle.LoadAsset(asset_path);
                }
                else
                {
                    RCMManager.Log("CRITICAL FAILURE!!!! bundle not found: " + asset_bundle);
                    return null;
                }

            }
            else return Resources.Load(path);
        }

        // WARNING: code needs to be updated to match game
        [HarmonyPatch(typeof(EntityFactory), "InstantiateEntity", new Type[] { typeof(string), typeof(Vector3), typeof(EntityController), typeof(string), typeof(string), typeof(Transform), typeof(UnitRole), typeof(bool), typeof(string) })]
        private static class EntityFactoryPatch1
        {
            public static bool Prefix(ref EntityController __result, string entityId, Vector3 position, EntityController originEntity, string tag, string name, Transform parentTransform, UnitRole additionalRoles, bool hasBeenCalledFromAbove, string instantiationInfo)
            {

                RCMManager.Log("entity instantiation hook: " + entityId);

                //string path = EntityBalancingStore.PrefabLocation(entityId);
                //GameObject gameObject = (((object)parentTransform == null) ? ((GameObject)UnityEngine.Object.Instantiate(load_from_bundle_or_resource(path), position, Quaternion.identity))
                //                                                           : ((GameObject)UnityEngine.Object.Instantiate(load_from_bundle_or_resource(path), position, Quaternion.identity, parentTransform)));
                //if (name != "") gameObject.name = name;
                //if (!string.IsNullOrEmpty(tag)) gameObject.tag = tag;
                //EntityController component = gameObject.GetComponent<EntityController>();
                //component.Init(originEntity);
                //component.SetUniqueEntityId(EntityFactory.NextUniqueEntityId());
                //component.AddRoles(additionalRoles);
                //gameObject.SetActive(value: true);
                //if (Tags.IsPlayer(tag)){
                //    component.ReplaceMaterial(EntityFactory.PlayerMaterial);
                //    component.SetHealthBarColor(GameBalancingStore.PlayerHealthBarColor, GameBalancingStore.PlayerHealthBarBatteredColor);
                //    if (Game.CardUpgrades.TryGetValue(entityId, out var value))
                //    {
                //        foreach (string item in value)
                //            component.AddUpgrade(item);
                //    }

                //} else if (Tags.IsAi(tag)){
                //    component.ReplaceMaterial(component.IsBuilding ? EntityFactory.AiBuildingMaterial : EntityFactory.AiMaterial);
                //    component.SetHealthBarColor(GameBalancingStore.AiHealthBarColor, GameBalancingStore.AiHealthBarBatteredColor);
                //    if (!component.HasRole(UnitRole.Spawn) && component.IsBuilding)
                //        EntityFactory.AiBuildingCount++;
                //}
                //component.OnHasBeenInstantiated(hasBeenCalledFromAbove);
                //__result = component;

                //return false;
                if (!WorldGrid.Instance.IsWithinWorld(position))
                {
                    ExceptionEvent.Record("EntityFactory: Entity should be instantiated outside the map", null, new object[] { entityId, instantiationInfo, position });
                    //return null;
                    __result = null;
                    return false;
                }
                Stack<EntityController> stack;
                EntityController entityController;
                GameObject gameObject;
                if (EntityFactory.PrespawnedEntities.TryGetValue(entityId, out stack) && stack.Count > 0)
                {
                    entityController = stack.Pop();
                    gameObject = entityController.gameObject;
                    if (parentTransform != null)
                    {
                        gameObject.transform.SetParent(parentTransform, false);
                    }
                    entityController.transform.position = position;
                }
                else
                {
                    string text = EntityBalancingStore.PrefabLocation(entityId);
                    if (parentTransform != null)
                    {
                        gameObject = (GameObject)UnityEngine.Object.Instantiate(load_from_bundle_or_resource(text), position, Quaternion.identity, parentTransform);
                    }
                    else
                    {
                        gameObject = (GameObject)UnityEngine.Object.Instantiate(load_from_bundle_or_resource(text), position, Quaternion.identity);
                    }
                    entityController = gameObject.GetComponent<EntityController>();
                }
                if (name != "")
                {
                    gameObject.name = name;
                }
                GameObject gameObject2 = gameObject;
                gameObject2.name = gameObject2.name + " by " + instantiationInfo;
                if (!string.IsNullOrEmpty(tag))
                {
                    gameObject.tag = tag;
                }
                entityController.Init(originEntity);
                entityController.SetUniqueEntityId(EntityFactory.NextUniqueEntityId());
                entityController.AddRoles(additionalRoles);
                gameObject.SetActive(true);
                if (Tags.IsPlayer(tag))
                {
                    entityController.ReplaceMaterial(EntityFactory.PlayerMaterial);
                    List<string> list;
                    if (!Game.CardUpgrades.TryGetValue(entityId, out list))
                    {
                        goto IL_01B0;
                    }
                    using (List<string>.Enumerator enumerator = list.GetEnumerator())
                    {
                        while (enumerator.MoveNext())
                        {
                            string text2 = enumerator.Current;
                            entityController.AddUpgrade(text2);
                        }
                        goto IL_01B0;
                    }
                }
                if (Tags.IsAi(tag))
                {
                    entityController.ReplaceMaterial(entityController.IsBuilding ? EntityFactory.AiBuildingMaterial : EntityFactory.AiMaterial);
                    if (!entityController.HasRole(UnitRole.Spawn) && entityController.IsBuilding)
                    {
                        EntityFactory.AiBuildingCount++;
                    }
                }
            IL_01B0:
                EntityFactory.SetHealthBarColor(entityController);
                entityController.OnHasBeenInstantiated(hasBeenCalledFromAbove);
                __result = entityController;

                return false;
            }
        }





        // WARNING: code needs to be updated to match game
        [HarmonyPatch(typeof(EntityFactory), "CreateBuildingPlacementGhost", new Type[] { typeof(string), typeof(bool) })]
        private static class EntityFactoryPatch2
        {
            public static bool Prefix(ref GameObject __result, string entityId, bool returnInactive)
            {
                RCMManager.Log("ghost building placement hook: " + entityId);

                //GameObject gameObject = (GameObject)UnityEngine.Object.Instantiate(load_from_bundle_or_resource(EntityBalancingStore.PrefabLocation(entityId)));
                //EntityController component = gameObject.GetComponent<EntityController>();
                //EntityController.RangeToVisualize visualizeRangeWhileSelected = component.visualizeRangeWhileSelected;
                //UnityEngine.Object.Destroy(component);
                //UnityEngine.Object.Destroy(gameObject.GetComponent<EntityBaseParameters>());
                //UnityEngine.Object.Destroy(gameObject.GetComponent<BoxCollider>());
                //AudioSource[] components = gameObject.GetComponents<AudioSource>();
                //for (int i = 0; i < components.Length; i++)
                //    UnityEngine.Object.Destroy(components[i]);

                //UnityEngine.Object.Destroy(gameObject.transform.Find("FogOfWarVisibleArea").gameObject);
                //UnityEngine.Object.Destroy(gameObject.transform.Find("MinimapShape").gameObject);
                //if (visualizeRangeWhileSelected != 0){
                //    GameObject gameObject2 = UnityEngine.Object.Instantiate(EntityFactory.RangeCirclePrefab, gameObject.transform, worldPositionStays: true);
                //    gameObject2.name = "PlacementGhostRangeCircle";
                //    float num;
                //    switch (visualizeRangeWhileSelected){
                //        case EntityController.RangeToVisualize.No:
                //            num = 0f;
                //            break;
                //        case EntityController.RangeToVisualize.SkillRange:
                //            num = (float)EntityBalancingStore.SkillRange(entityId);
                //            break;
                //        case EntityController.RangeToVisualize.WeaponRange:
                //            num = EntityBalancingStore.WeaponRange(entityId, false);
                //            break;
                //        case EntityController.RangeToVisualize.SightRange:
                //            num = (float)EntityBalancingStore.SightRadius(entityId, false);
                //            break;
                //        case EntityController.RangeToVisualize.EffectRadius1:
                //            num = EntityBalancingStore.EffectRadius1(entityId);
                //            break;
                //        case EntityController.RangeToVisualize.EffectRadius2:
                //            num = EntityBalancingStore.EffectRadius2(entityId);
                //            break;
                //        default:
                //            throw new ArgumentOutOfRangeException();
                //    }
                //    gameObject2.transform.localScale = 10f * num * Vector3.one;
                //}
                //if (!returnInactive)
                //{
                //    gameObject.SetActive(value: true);
                //}
                //__result = gameObject;
                //return false;
                GameObject gameObject = (GameObject)UnityEngine.Object.Instantiate(load_from_bundle_or_resource(EntityBalancingStore.PrefabLocation(entityId)));
                GameObject gameObject2 = gameObject;
                gameObject2.name = gameObject2.name + " by BuildingPlacementGhost " + entityId;
                EntityController component = gameObject.GetComponent<EntityController>();
                EntityController.RangeToVisualize visualizeRangeWhileSelected = component.visualizeRangeWhileSelected;
                UnityEngine.Object.Destroy(component);
                UnityEngine.Object.Destroy(gameObject.GetComponent<EntityBaseParameters>());
                UnityEngine.Object.Destroy(gameObject.GetComponent<BoxCollider>());
                AudioSource[] components = gameObject.GetComponents<AudioSource>();
                for (int i = 0; i < components.Length; i++)
                {
                    UnityEngine.Object.Destroy(components[i]);
                }
                UnityEngine.Object.Destroy(gameObject.transform.Find("FogOfWarVisibleArea").gameObject);
                UnityEngine.Object.Destroy(gameObject.transform.Find("MinimapShape").gameObject);
                if (visualizeRangeWhileSelected != EntityController.RangeToVisualize.No)
                {
                    GameObject gameObject3 = UnityEngine.Object.Instantiate<GameObject>(EntityFactory.RangeCirclePrefab, gameObject.transform, true);
                    gameObject3.name = "PlacementGhostRangeCircle";
                    float num;
                    switch (visualizeRangeWhileSelected)
                    {
                        case EntityController.RangeToVisualize.No:
                            num = 0f;
                            break;
                        case EntityController.RangeToVisualize.SkillRange:
                            num = (float)EntityBalancingStore.SkillRange(entityId, false);
                            break;
                        case EntityController.RangeToVisualize.WeaponRange:
                            num = EntityBalancingStore.WeaponRange(entityId, false);
                            break;
                        case EntityController.RangeToVisualize.SightRange:
                            num = (float)EntityBalancingStore.SightRadius(entityId, false);
                            break;
                        case EntityController.RangeToVisualize.EffectRadius1:
                            num = EntityBalancingStore.EffectRadius1(entityId, false);
                            break;
                        case EntityController.RangeToVisualize.EffectRadius2:
                            num = EntityBalancingStore.EffectRadius2(entityId, false);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    float num2 = num;
                    gameObject3.transform.localScale = 10f * num2 * Vector3.one;
                }
                if (!returnInactive)
                {
                    gameObject.SetActive(true);
                }
                __result = gameObject;
                return false;
            }
        }

        // WARNING: code needs to be updated to match game
        [HarmonyPatch(typeof(EntityFactory), "CreateEntityMesh", new Type[] { typeof(string), typeof(Transform), typeof(float) })]
        private static class EntityFactoryPatch3
        {
            public static bool Prefix(GameObject __result, string entityId, Transform parentTransform, float scaleMultiplier = 1f)
            {
                RCMManager.Log("entity mesh creation hook: " + entityId);

                //   string text = EntityBalancingStore.PrefabLocation(entityId);
                //   UnityEngine.Object @object = load_from_bundle_or_resource(text);
                //   if (@object == null){
                //    Logger.Warn("Cannot find prefab (" + text + ") for entityId: " + entityId);
                //    __result = null;
                //             return false;
                //   }
                //   GameObject gameObject = (GameObject)UnityEngine.Object.Instantiate(@object, parentTransform);
                //   gameObject.SetLayerRecursively(22, 24);
                //   gameObject.SetActive(value: true);
                //   if (Math.Abs(scaleMultiplier - 1f) > 1E-06f)
                //    gameObject.transform.localScale *= scaleMultiplier;

                //   UnityEngine.Object.Destroy(gameObject.GetComponent<EntityController>());
                //   UnityEngine.Object.Destroy(gameObject.GetComponent<EntityBaseParameters>());
                //   UnityEngine.Object.Destroy(gameObject.GetComponent<BoxCollider>());
                //   AudioSource[] components = gameObject.GetComponents<AudioSource>();
                //   for (int i = 0; i < components.Length; i++)
                //             UnityEngine.Object.Destroy(components[i]);
                //   UnityEngine.Object.Destroy(gameObject.GetComponentInChildren<ScaleByChangeableValue>());


                //   Transform transform = gameObject.transform.Find("UnitSpawnedEffect");
                //   if ((bool)transform) UnityEngine.Object.Destroy(transform.gameObject);

                //   Transform transform2 = gameObject.transform.Find("BarCanvases");
                //   if ((bool)transform2) UnityEngine.Object.Destroy(transform2.gameObject);

                //   Transform transform3 = gameObject.transform.Find("SelectionCircles");
                //   if ((bool)transform3) UnityEngine.Object.Destroy(transform3.gameObject);

                //   Transform transform4 = gameObject.transform.Find("MinimapShape");
                //   if ((bool)transform4) UnityEngine.Object.Destroy(transform4.gameObject);

                //__result = gameObject;
                //         return false;
                string text = EntityBalancingStore.PrefabLocation(entityId);
                UnityEngine.Object @object = load_from_bundle_or_resource(text);
                if (@object == null)
                {
                    global::Logger.Warn("Cannot find prefab (" + text + ") for entityId: " + entityId);
                    __result = null;
                    return false;
                }
                GameObject gameObject = (GameObject)UnityEngine.Object.Instantiate(@object, parentTransform);
                GameObject gameObject2 = gameObject;
                gameObject2.name = gameObject2.name + " by CreateEntityMesh " + entityId;
                gameObject.SetLayerRecursively(22, 24);
                gameObject.SetActive(true);
                if (Math.Abs(scaleMultiplier - 1f) > 1E-06f)
                {
                    gameObject.transform.localScale *= scaleMultiplier;
                }
                EntityController component = gameObject.GetComponent<EntityController>();
                if (component)
                {
                    foreach (EntityController entityController in component.childEntityControllers)
                    {
                        UnityEngine.Object.Destroy(entityController);
                    }
                    UnityEngine.Object.Destroy(component);
                }
                UnityEngine.Object.Destroy(gameObject.GetComponent<EntityBaseParameters>());
                UnityEngine.Object.Destroy(gameObject.GetComponent<BoxCollider>());
                AudioSource[] components = gameObject.GetComponents<AudioSource>();
                for (int i = 0; i < components.Length; i++)
                {
                    UnityEngine.Object.Destroy(components[i]);
                }
                UnityEngine.Object.Destroy(gameObject.GetComponentInChildren<ScaleByChangeableValue>());
                Transform transform = gameObject.transform.Find("UnitSpawnedEffect");
                if (transform)
                {
                    UnityEngine.Object.Destroy(transform.gameObject);
                }
                Transform transform2 = gameObject.transform.Find("BarCanvases2024");
                if (transform2)
                {
                    UnityEngine.Object.Destroy(transform2.gameObject);
                }
                Transform transform3 = gameObject.transform.Find("SelectionCircles");
                if (transform3)
                {
                    UnityEngine.Object.Destroy(transform3.gameObject);
                }
                Transform transform4 = gameObject.transform.Find("MinimapShape");
                if (transform4)
                {
                    UnityEngine.Object.Destroy(transform4.gameObject);
                }
                __result = gameObject;
                return false;
            }
        }

        // debug loaded card info code patch stuff
        /*
        //[HarmonyPatch(typeof(Glossary), "SetupUnknownCardsCache")]
        //private static class debug1Patch{
        //    private static void Postfix(Glossary __instance){
        //        RCMManager.Log("logging  cardSlotTransforms: " + __instance.cardSlotTransforms.Count);
        //        RCMManager.Log("logging  Infos.Starters: " + __instance._filteredCardInfos[Filter.Starters].Count);
        //        RCMManager.Log("logging  Infos.Blueprints: " + __instance._filteredCardInfos[Filter.Blueprints].Count);
        //        RCMManager.Log("logging  Infos.Ai: " + __instance._filteredCardInfos[Filter.Ai].Count);
        //        RCMManager.Log("logging  page: " + __instance._currentPage);
        //        RCMManager.Log("logging  _unknownCardsCache: " + __instance._unknownCardsCache.Count);
        //        RCMManager.Log("logging  _unknownUpgradesCache: " + __instance._unknownUpgradesCache.Count);
        //    }
        //}
        //[HarmonyPatch(typeof(Glossary), "UpdateCardView")]
        //private static class debug2Patch{
        //    private static void Prefix(Glossary __instance){
        //        RCMManager.Log("logging  cardSlotTransforms: " + __instance.cardSlotTransforms.Count);
        //        RCMManager.Log("logging  Infos.Starters: " + __instance._filteredCardInfos[Filter.Starters].Count);
        //        RCMManager.Log("logging  Infos.Blueprints: " + __instance._filteredCardInfos[Filter.Blueprints].Count);
        //        RCMManager.Log("logging  Infos.Ai: " + __instance._filteredCardInfos[Filter.Ai].Count);
        //        RCMManager.Log("logging  page: " + __instance._currentPage);
        //        RCMManager.Log("logging  _unknownCardsCache: " + __instance._unknownCardsCache.Count);
        //        RCMManager.Log("logging  _unknownUpgradesCache: " + __instance._unknownUpgradesCache.Count);
        //    }
        //}
        */
    }
}
