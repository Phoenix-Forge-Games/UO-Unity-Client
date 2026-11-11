// Interest management: each client is sent only the networked objects within 100 m of its character (hidden again
// past 110 m). The server still runs everything. Applies to the open Game scene's "Network" object; MakeGameScene
// does the same when it builds the scene.
const string ConditionPath = "Assets/UOClient/Game/ActorsInRange.asset";
var condition = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Component.Observing.DistanceCondition>(ConditionPath);
if (condition == null)
{
    condition = UnityEngine.ScriptableObject.CreateInstance<FishNet.Component.Observing.DistanceCondition>();
    UnityEditor.AssetDatabase.CreateAsset(condition, ConditionPath);
}

var cso = new UnityEditor.SerializedObject(condition);
cso.FindProperty("_maximumDistance").floatValue = 100f;
cso.FindProperty("_hideDistancePercent").floatValue = 0.1f;
cso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.AssetDatabase.SaveAssets();

var net = UnityEngine.GameObject.Find("Network");
var observers = net.GetComponent<FishNet.Managing.Observing.ObserverManager>() ?? net.AddComponent<FishNet.Managing.Observing.ObserverManager>();
var oso = new UnityEditor.SerializedObject(observers);
var list = oso.FindProperty("_defaultConditions");
list.ClearArray();
list.InsertArrayElementAtIndex(0);
list.GetArrayElementAtIndex(0).objectReferenceValue = condition;
// Walking pace is 7 m/s and the hide margin is 10 m: check every object at least once a second.
oso.FindProperty("_maximumTimedObserversDuration").floatValue = 1f;
oso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return $"observers: {observers.GetType().Name}, condition {condition.GetMaximumDistance()} m";
