﻿using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using JetBrains.Annotations;
using DunGen.Graph;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System;
using System.Reflection;
using Zeekerss.Core.Singletons;
using LethalFoundation;

namespace LethalLevelLoader
{
    public class ExtendedNetworkManager : NetworkSingleton<ExtendedNetworkManager>
    {
        public static ExtendedNetworkManager Instance => NetworkInstance as ExtendedNetworkManager;
        private static Dictionary<GameObject, NetworkPrefab> NetworkPrefabRegistry = new Dictionary<GameObject, NetworkPrefab>();
        private static Dictionary<string, GameObject> VanillaNetworkPrefabNameDict = new Dictionary<string, GameObject>();
        private static List<GameObject> queuedInternalNetworkPrefabs = new List<GameObject>();
        private static Dictionary<ExtendedMod, List<GameObject>> networkPrefabCollections = new Dictionary<ExtendedMod, List<GameObject>>();
        public static bool networkHasStarted;

        private static List<NetworkSingleton> queuedNetworkSingletonSpawns = new List<NetworkSingleton>();

        public static bool IsLobbyNetworkInitialized { get; private set; }

        [RuntimeInitializeOnLoadMethod]
        private static void Init()
        {
            DebugHelper.Log("Inherited Init Is Working Yippeeeee", DebugType.User);
            Events.OnCurrentStateChanged.AddListener(OnGameStateChanged);
        }

        private static void OnGameStateChanged(GameStates state)
        {
            if (state == GameStates.MainMenu)
                IsLobbyNetworkInitialized = false;
        }

        internal static T CreateAndRegisterNetworkSingleton<T>(Type realType,  string name, bool dontDestroyWithOwner = false, bool sceneMigration = true, bool destroyWithScene = true) where T : NetworkSingleton
        {
            var prefab = Utilities.CreateNetworkPrefab<T>(realType, name, dontDestroyWithOwner, sceneMigration, destroyWithScene);
            queuedNetworkSingletonSpawns.Add(prefab);
            return (prefab);
        }

        static int activeNetworkSingletonSpawnCounter;

        internal static void SpawnNetworkSingletons()
        {
            activeNetworkSingletonSpawnCounter = queuedNetworkSingletonSpawns.Count;
            if (!NetworkManagerInstance.IsServer) return;
            DebugHelper.Log("Spawning: " + activeNetworkSingletonSpawnCounter + " NetworkSingletons!", DebugType.User);
            foreach (NetworkSingleton singletonPrefab in queuedNetworkSingletonSpawns)
            {
                DebugHelper.Log("Spawning NetworkSingleton: " + singletonPrefab.name, DebugType.User);
                Instantiate(singletonPrefab).GetComponent<NetworkObject>().Spawn(true); //MAKE NOT TRUE LATER
            }
        }

        internal static void OnNetworkSingletonSpawned(NetworkSingleton singleton)
        {
            DebugHelper.Log("NetworkSingleton: " + singleton.name + " Has Spawned!", DebugType.User);
            activeNetworkSingletonSpawnCounter--;
            if (activeNetworkSingletonSpawnCounter == 0)
                OnNetworkSingletonsSpawned();
        }

        internal static void OnNetworkSingletonsSpawned()
        {
            DebugHelper.Log("All Registered NetworkSingletons Have Spawned!", DebugType.User);
            IsLobbyNetworkInitialized = true;
        }

        protected override void OnNetworkSingletonSpawn()
        {
            gameObject.name = "LethalLevelLoaderNetworkManager";
            DebugHelper.Log("LethalLevelLoaderNetworkManager Spawned.", DebugType.User);
        }

        public static void TryRefreshWeather()
        {
            if (IsSpawnedAndIntialized)
                Instance.GetUpdatedLevelCurrentWeatherServerRpc();
        }

        [ServerRpc]
        public void GetRandomExtendedDungeonFlowServerRpc()
        {
            DebugHelper.Log("Getting Random DungeonFlows!", DebugType.User);

            List<ExtendedDungeonFlowWithRarity> availableFlows = DungeonManager.GetValidExtendedDungeonFlows(LevelManager.CurrentExtendedLevel, debugResults: true);
            NetworkValueWithRarity<NetworkContentReference<ExtendedDungeonFlow>>[] flows = new NetworkValueWithRarity<NetworkContentReference<ExtendedDungeonFlow>>[availableFlows.Count];
            if (availableFlows.Count == 0)
            {
                DebugHelper.LogError("No ExtendedDungeonFlow's could be found! This should only happen if the Host's requireMatchesOnAllDungeonFlows is set to true!", DebugType.User);
                DebugHelper.LogError("Loading Facility DungeonFlow to prevent infinite loading!", DebugType.User);
                flows[0] = new(Refs.DungeonFlowTypes[0].dungeonFlow.AsExtended(), 300);
            }
            else
                flows = availableFlows.Select(f => new NetworkValueWithRarity<NetworkContentReference<ExtendedDungeonFlow>>(f.extendedDungeonFlow, f.rarity)).ToArray();
            SetRandomExtendedDungeonFlowClientRpc(flows);
        }

        [ServerRpc]
        private void GetUpdatedLevelCurrentWeatherServerRpc()
        {
            List<NetworkString> levelNames = new List<NetworkString>();
            List<LevelWeatherType> weatherTypes = new List<LevelWeatherType>();
            foreach (ExtendedLevel extendedLevel in PatchedContent.ExtendedLevels)
            {
                levelNames.Add(new NetworkString(extendedLevel.name));
                weatherTypes.Add(extendedLevel.SelectableLevel.currentWeather);
            }

            SetUpdatedLevelCurrentWeatherClientRpc(levelNames.ToArray(), weatherTypes.ToArray());
        }

        [ClientRpc]
        public void SetUpdatedLevelCurrentWeatherClientRpc(NetworkString[] levelNames, LevelWeatherType[] weatherTypes)
        {
            Dictionary<ExtendedLevel, LevelWeatherType> syncedLevelCurrentWeathers = new Dictionary<ExtendedLevel, LevelWeatherType>();

            for (int i = 0; i < levelNames.Length; i++)
                foreach (ExtendedLevel extendedLevel in PatchedContent.ExtendedLevels)
                    if (levelNames[i].StringValue == extendedLevel.name)
                        syncedLevelCurrentWeathers.Add(extendedLevel, weatherTypes[i]);

            foreach (KeyValuePair<ExtendedLevel, LevelWeatherType> syncedWeather in syncedLevelCurrentWeathers)
            {
                if (syncedWeather.Key.SelectableLevel.currentWeather != syncedWeather.Value)
                {
                    DebugHelper.LogWarning("Client Had Differing Current Weather Value For ExtendedLevel: " + syncedWeather.Key.NumberlessPlanetName + ", Syncing!", DebugType.User);
                    syncedWeather.Key.SelectableLevel.currentWeather = syncedWeather.Value;
                }
            }
        }

        [ClientRpc]
        public void SetRandomExtendedDungeonFlowClientRpc(NetworkValueWithRarity<NetworkContentReference<ExtendedDungeonFlow>>[] dungeons)
        {
            DebugHelper.Log("Setting Random DungeonFlows!", DebugType.User);
            IntWithRarity[] cachedDungeons = Refs.CurrentLevel.dungeonFlowTypes;

            Refs.CurrentLevel.dungeonFlowTypes = dungeons.Select(d => Utilities.Create(d.Value.GetContent().GameID, d.Rarity)).ToArray();
            Refs.RoundManager.GenerateNewFloor();
            Refs.CurrentLevel.dungeonFlowTypes = cachedDungeons;
        }

        [ServerRpc]
        public void GetDungeonFlowSizeServerRpc()
        {
            SetDungeonFlowSizeClientRpc(DungeonLoader.GetClampedDungeonSize());
        }

        [ClientRpc]
        public void SetDungeonFlowSizeClientRpc(float hostSize)
        {
            if (Refs.RuntimeDungeon == null) return;
            Refs.DungeonGenerator.LengthMultiplier = hostSize;
            Refs.RuntimeDungeon.Generate();
        }

        [ServerRpc]
        internal void SetExtendedLevelValuesServerRpc(ExtendedLevelData extendedLevelData)
        {
            if (PatchedContent.TryGetExtendedContent(extendedLevelData.UniqueIdentifier, out ExtendedLevel extendedLevel))
                SetExtendedLevelValuesClientRpc(extendedLevelData);
            else
                DebugHelper.Log("Failed To Send Level Info!", DebugType.User);
        }
        [ClientRpc]
        internal void SetExtendedLevelValuesClientRpc(ExtendedLevelData extendedLevelData)
        {
            if (PatchedContent.TryGetExtendedContent(extendedLevelData.UniqueIdentifier, out ExtendedLevel extendedLevel))
                extendedLevelData.ApplySavedValues(extendedLevel);
            else
                DebugHelper.Log("Failed To Apply Saved Level Info!", DebugType.User);
        }

        internal static void RegisterNetworkPrefab(ExtendedMod source, GameObject prefab)
        {
            if (prefab == null || source == null ) return;
            if (networkHasStarted == false)
                networkPrefabCollections.AddOrAddAdd(source, prefab);
            else
                DebugHelper.LogWarning("Attempted To Register NetworkPrefab: " + prefab + " After GameNetworkManager Has Started!", DebugType.User);
        }

        internal static void RegisterNetworkPrefab(GameObject prefab)
        {
            if (prefab == null || queuedInternalNetworkPrefabs.Contains(prefab)) return;
            if (networkHasStarted == false)
                queuedInternalNetworkPrefabs.Add(prefab);
            else
                DebugHelper.LogWarning("Attempted To Register NetworkPrefab: " + prefab + " After GameNetworkManager Has Started!", DebugType.User);
        }

        internal static void RegisterNetworkContent(ExtendedContent content)
        {
            List<GameObject> registeredObjects = new List<GameObject>();

            foreach (GameObject networkPrefab in content.GetNetworkPrefabsForRegistration())
                if (TryRegisterNetworkPrefab(networkPrefab))
                    registeredObjects.Add(networkPrefab);

            foreach (PrefabReference networkPrefabReference in content.GetPrefabReferencesForRestorationOrRegistration())
                if (TryRegisterNetworkPrefabReference(networkPrefabReference))
                    registeredObjects.Add(networkPrefabReference.Prefab);

            foreach (GameObject prefab in registeredObjects)
                networkPrefabCollections.AddOrAddAdd(content.ExtendedMod, prefab);
        }

        internal static void TrackVanillaPrefabs()
        {
            DebugHelper.Log(Events.FurthestState + "yyyyy", DebugType.User);
            if (Events.FurthestState == GameStates.Lobby || Events.FurthestState == GameStates.Moon) return;
            foreach (NetworkPrefab networkPrefab in Refs.NetworkPrefabsList.PrefabList)
            {
                AddNetworkPrefabToRegistry(networkPrefab);
                networkPrefabCollections.AddOrAddAdd(PatchedContent.VanillaMod, networkPrefab.Prefab);
                VanillaNetworkPrefabNameDict.Add(networkPrefab.Prefab.name, networkPrefab.Prefab);
            }
        }

        internal static void RegisterInternalNetworkPrefabs()
        {
            //Register LethalLevelLoader's various Managers (Tracked via NetworkPrefabHandler Postfix)
            foreach (GameObject queuedPrefab in queuedInternalNetworkPrefabs)
                TryRegisterNetworkPrefab(queuedPrefab);
            networkHasStarted = true;      
        }

        private static bool TryRegisterNetworkPrefabReference(PrefabReference prefabReference)
        {
            if (VanillaNetworkPrefabNameDict.TryGetValue(prefabReference.Prefab.name, out GameObject vanillaPrefab))
            {
                prefabReference.Restore(vanillaPrefab);
                return (false);
            }
            else
                return (TryRegisterNetworkPrefab(prefabReference.Prefab));         
        }

        private static bool TryRegisterNetworkPrefab(GameObject gameObject)
        {
            if (gameObject == null) return (false);
            if (NetworkPrefabRegistry.ContainsKey(gameObject)) return (false);
            if (gameObject.TryGetComponent(out NetworkObject networkObject) == false) return (false);
            return (TryRegisterNetworkPrefab(networkObject));
        }

        private static bool TryRegisterNetworkPrefab(NetworkObject networkObject)
        {
            if (networkObject == null) return (false);
            if (NetworkPrefabRegistry.ContainsKey(networkObject.gameObject)) return (false);
            NetworkManagerInstance.AddNetworkPrefab(networkObject.gameObject);
            return (true);
        }

        //This gets called when we can access the NetworkManager for the first time and via a postfix to AddNetworkPrefab so we catch other mods too
        internal static void AddNetworkPrefabToRegistry(NetworkPrefab registeredPrefab)
        {
            if (NetworkPrefabRegistry.ContainsKey(registeredPrefab.Prefab)) return;
            NetworkPrefabRegistry.Add(registeredPrefab.Prefab, registeredPrefab);
        }
    }
}
