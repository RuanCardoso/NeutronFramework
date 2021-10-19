using NeutronNetwork.Internal.Packets;
using NeutronNetwork.Server.Internal;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeutronNetwork.Helpers
{
    public static class SceneHelper
    {
        public static PhysicsManager CreateContainer(string name, LocalPhysicsMode physics = LocalPhysicsMode.None)
        {
            Scene fScene = SceneManager.GetSceneByName(name);
            if (!fScene.IsValid())
            {
                Scene newScene = SceneManager.CreateScene(name, new CreateSceneParameters(physics));
                //* Cria um gerenciador de f�sica.
                GameObject parent = new GameObject("Physics Manager");
                parent.hideFlags = HideFlags.HideInHierarchy;
                PhysicsManager manager = parent.AddComponent<PhysicsManager>();
                manager.Scene = newScene;
                manager.PhysicsScene = newScene.GetPhysicsScene();
                manager.PhysicsScene2D = newScene.GetPhysicsScene2D();
                //* Move o gerenciador de f�sica para a sua cena em quest�o.
                MoveToContainer(parent, newScene.name);
                return manager;
            }
            else
                return null;
        }

        public static void MoveToContainer(GameObject obj, string name)
        {
            Scene dstScene = SceneManager.GetSceneByName(name);
            if (dstScene.IsValid())
                SceneManager.MoveGameObjectToScene(obj.transform.root.gameObject, SceneManager.GetSceneByName(name));
            else
                LogHelper.Error($"Container {name} not found!");
        }

        public static void MoveToContainer(GameObject obj, Scene scene)
        {
            if (scene.IsValid())
                SceneManager.MoveGameObjectToScene(obj.transform.root.gameObject, scene);
            else
                LogHelper.Error("Scene is not valid!");
        }

        public static GameObject MakeMatchmakingManager(NeutronPlayer player, bool isServer, Neutron neutron)
        {
            //* Inicializa um Matchmaking Manager e o registra na rede.
            GameObject matchManager = new GameObject("Match Manager");
            //matchManager.hideFlags = HideFlags.HideInHierarchy;
            var neutronView = matchManager.AddComponent<NeutronView>();
            neutronView.AutoDestroy = false;
            //* Inicializa o iRpc Actions baseado no tipo.
            NeutronBehaviour[] actions = Neutron.Server.Actions;

            #region Server Player
            NeutronPlayer owner = player;
            if (Neutron.Server.MatchmakingManagerOwner == OwnerMode.Server)
                owner = PlayerHelper.MakeTheServerPlayer(player.Channel, player.Room, player.Matchmaking);
            #endregion

            if (actions.Length > 0)
            {
                GameObject actionsObject = GameObject.Instantiate(actions[actions.Length - 1].gameObject, matchManager.transform);
                actionsObject.name = "Actions Object";
                foreach (Component component in actionsObject.GetComponents<Component>())
                {
                    Type type = component.GetType();
                    if (type.BaseType != typeof(NeutronBehaviour) && type != typeof(Transform))
                        GameObject.Destroy(component);
                }
            }
            neutronView.OnNeutronRegister(owner, isServer, RegisterMode.Dynamic, neutron);
            return matchManager;
        }

        public static bool IsInScene(GameObject gameObject)
        {
            return gameObject.scene.IsValid();
        }
    }
}