using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

// Gera tudo que não é código do jogo: clips extraídos dos FBX, Animator, materiais,
// prefabs, labirinto, NavMesh e a cena "Labirinto".
// Menu: Tools > Soco no Labirinto > Reconstruir projeto
// Em batch mode: Unity -batchmode -quit -projectPath <pasta> -executeMethod ProjectBuilder.Build
public static class ProjectBuilder
{
    // ---------- Caminhos
    const string ScenePath = "Assets/Scenes/Labirinto.unity";
    const string NavMeshAssetPath = "Assets/Scenes/Labirinto/NavMesh-Maze.asset";
    const string FighterFbx = "Assets/Models/Fighter.fbx";
    const string WalkFbx = "Assets/Animations/Source/Walk.fbx";
    const string PunchFbx = "Assets/Animations/Source/Punch.fbx";
    const string ControllerPath = "Assets/Animations/Enemy.controller";
    const string EnemyPrefabPath = "Assets/Prefabs/Enemy.prefab";
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

    // ---------- Labirinto ('#' parede, '.' corredor). Linha 0 fica no topo da tela.
    const float CellSize = 3f;
    const float WallHeight = 3f;

    static readonly string[] MazeMap =
    {
        "###########",
        "#...#.....#",
        "###.#.###.#",
        "#.#.#...#.#",
        "#.#.#.#.#.#",
        "#.#...#...#",
        "#.###.###.#",
        "#.........#",
        "###########",
    };

    // (coluna, linha) no mapa acima
    static readonly Vector2Int PlayerStart = new Vector2Int(1, 1);

    // O inimigo nasce no primeiro ponto e dá a volta pelos corredores.
    static readonly Vector2Int[] EnemyRoute =
    {
        new Vector2Int(9, 7), new Vector2Int(1, 7), new Vector2Int(3, 5), new Vector2Int(9, 3),
    };

    // ---------- Inimigo
    // O clip Walk anda a ~1,43 m/s (medido pela passada dos pés). A patrulha toca o clip a 1x e a
    // perseguição a 2x (estado Chase do Animator), então as velocidades do agente seguem o clip
    // para os pés não deslizarem no chão.
    const float WalkClipSpeed = 1.43f;
    const float EnemyPatrolSpeed = WalkClipSpeed;
    const float EnemyChaseSpeed = WalkClipSpeed * 2f;
    // Alcance de um soco: a mão avança ~0,64 m e o corpo do jogador tem raio 0,4,
    // então com 1 m o punho encosta no jogador.
    const float EnemyAttackRange = 1f;

    class Palette
    {
        public Material floor, wall, playerBody, playerVisor, playerStripe, enemySurface, enemyJoints;
    }

    [MenuItem("Tools/Soco no Labirinto/Reconstruir projeto")]
    static void BuildFromMenu()
    {
        // Reconstruir apaga qualquer ajuste feito à mão na cena e nos prefabs.
        if (EditorUtility.DisplayDialog(
                "Reconstruir projeto",
                "Isso recria a cena Labirinto, os prefabs, o controller e os clips, " +
                "descartando alterações feitas à mão (rota de patrulha, posições, etc.). Continuar?",
                "Reconstruir", "Cancelar"))
        {
            Build();
        }
    }

    public static void Build()
    {
        ValidateMaze();
        EnsureFolder("Assets/Scenes");
        EnsureFolder("Assets/Scenes/Labirinto");
        EnsureFolder("Assets/Materials");
        EnsureFolder("Assets/Prefabs");

        ConfigureImporters();

        AnimationClip idle = ExtractClip(FighterFbx, "Idle");
        AnimationClip walk = ExtractClip(WalkFbx, "Walk");
        AnimationClip punch = ExtractClip(PunchFbx, "Punch");
        AnimatorController controller = BuildController(idle, walk, punch);

        Palette palette = BuildPalette();
        GameObject playerPrefab = BuildPlayerPrefab(palette);
        GameObject enemyPrefab = BuildEnemyPrefab(controller, palette);

        BuildScene(playerPrefab, enemyPrefab, palette);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ProjectBuilder] Projeto reconstruído: " + ScenePath);
    }

    // ------------------------------------------------------------------ Importação / clips

    static void ConfigureImporters()
    {
        foreach (string path in new[] { FighterFbx, WalkFbx, PunchFbx })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;

            // Walk e Punch são só esqueleto + animação (Mixamo "without skin").
            if (path != FighterFbx)
                importer.materialImportMode = ModelImporterMaterialImportMode.None;

            importer.SaveAndReimport();
        }
    }

    // Equivale a "Ctrl+D" no clip do FBX: gera um .anim editável com o mesmo conteúdo.
    static AnimationClip ExtractClip(string fbxPath, string clipName)
    {
        AnimationClip source = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        if (source == null)
            throw new Exception("Nenhum AnimationClip encontrado em " + fbxPath);

        string outPath = "Assets/Animations/" + clipName + ".anim";
        AnimationClip target = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);

        if (target == null)
        {
            target = new AnimationClip();
            EditorUtility.CopySerialized(source, target);
            AssetDatabase.CreateAsset(target, outPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        target.name = clipName;

        // Idle e Walk ciclam; o Punch também, porque o AttackState mantém o
        // parâmetro Attacking ligado enquanto o jogador estiver ao alcance.
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(target);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(target, settings);

        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();

        Debug.Log($"[ProjectBuilder] Clip extraído: {outPath} ({target.length:F2}s) de {fbxPath}");
        return target;
    }

    // ------------------------------------------------------------------ Animator

    static AnimatorController BuildController(AnimationClip idle, AnimationClip walk, AnimationClip punch)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Alert", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attacking", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState idleState = machine.AddState("Idle", new Vector3(220, -40, 0));
        AnimatorState patrolState = machine.AddState("Patrol", new Vector3(510, -10, 0));
        AnimatorState chaseState = machine.AddState("Chase", new Vector3(450, 220, 0));
        AnimatorState attackState = machine.AddState("Attack", new Vector3(700, 220, 0));

        idleState.motion = idle;
        patrolState.motion = walk;
        chaseState.motion = walk;
        chaseState.speed = 2f; // mesmo Walk, em ritmo de corrida
        attackState.motion = punch;
        machine.defaultState = idleState;

        // Idle -> Patrol: está andando
        AnimatorStateTransition t = Link(idleState, patrolState, 0.15f);
        t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Alert");
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Attacking");

        // Idle -> Chase: alerta
        t = Link(idleState, chaseState, 0.15f);
        t.AddCondition(AnimatorConditionMode.If, 0, "Alert");
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Attacking");

        // Patrol -> Idle: parou
        t = Link(patrolState, idleState, 0.15f);
        t.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Attacking");

        // Patrol -> Chase: alerta
        t = Link(patrolState, chaseState, 0.15f);
        t.AddCondition(AnimatorConditionMode.If, 0, "Alert");
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Attacking");

        // Chase -> Patrol: perdeu o jogador
        t = Link(chaseState, patrolState, 0.15f);
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Alert");
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Attacking");

        // Qualquer estado -> Attack
        AnimatorStateTransition toAttack = machine.AddAnyStateTransition(attackState);
        toAttack.hasExitTime = false;
        toAttack.hasFixedDuration = true;
        toAttack.duration = 0.12f;
        toAttack.canTransitionToSelf = false;
        toAttack.AddCondition(AnimatorConditionMode.If, 0, "Attacking");

        // Attack -> Idle: parou de atacar
        t = Link(attackState, idleState, 0.12f);
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Attacking");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = duration;
        return t;
    }

    // ------------------------------------------------------------------ Materiais

    static Palette BuildPalette()
    {
        return new Palette
        {
            floor = CreateMaterial("Floor", Hex("2E3B44"), 0.15f),
            wall = CreateMaterial("Wall", Hex("D8C7A6"), 0.1f),
            playerBody = CreateMaterial("PlayerBody", Hex("FF8A1F"), 0.35f),
            playerVisor = CreateMaterial("PlayerVisor", Hex("12161A"), 0.9f),
            playerStripe = CreateMaterial("PlayerStripe", Hex("19D3E6"), 0.5f),
            enemySurface = CreateMaterial("EnemySurface", Hex("B3261E"), 0.3f),
            enemyJoints = CreateMaterial("EnemyJoints", Hex("26292E"), 0.4f),
        };
    }

    static Material CreateMaterial(string name, Color color, float smoothness)
    {
        string path = "Assets/Materials/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }

        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Color Hex(string rgb)
    {
        ColorUtility.TryParseHtmlString("#" + rgb, out Color c);
        return c;
    }

    // ------------------------------------------------------------------ Prefabs

    // O pivô fica no meio do corpo (y = 1) porque o EnemySensor mira em player.position.
    static GameObject BuildPlayerPrefab(Palette palette)
    {
        var root = new GameObject("Player") { tag = "Player" };

        var cc = root.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.4f;
        cc.center = Vector3.zero;
        cc.stepOffset = 0.3f;
        cc.skinWidth = 0.05f;

        root.AddComponent<PlayerController>();

        Primitive(PrimitiveType.Capsule, "Body", root.transform,
            Vector3.zero, new Vector3(0.8f, 1f, 0.8f), palette.playerBody);
        Primitive(PrimitiveType.Cylinder, "Stripe", root.transform,
            new Vector3(0f, 0.1f, 0f), new Vector3(0.84f, 0.04f, 0.84f), palette.playerStripe);
        Primitive(PrimitiveType.Cube, "Visor", root.transform,
            new Vector3(0f, 0.55f, 0.28f), new Vector3(0.5f, 0.16f, 0.3f), palette.playerVisor);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject BuildEnemyPrefab(AnimatorController controller, Palette palette)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FighterFbx);
        var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
        root.name = "Enemy";

        foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            smr.sharedMaterial = smr.name == "Beta_Joints" ? palette.enemyJoints : palette.enemySurface;
        }

        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.radius = 0.4f;
        capsule.height = 1.8f;
        capsule.center = new Vector3(0f, 0.9f, 0f);

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.5f;
        agent.height = 2f;
        agent.speed = EnemyPatrolSpeed;
        agent.acceleration = 12f;
        agent.angularSpeed = 270f;
        agent.stoppingDistance = 0f;

        var animator = root.GetComponent<Animator>();
        if (animator == null)
            animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        var eye = new GameObject("EyePoint");
        eye.transform.SetParent(root.transform, false);
        eye.transform.localPosition = new Vector3(0f, 1.5f, 0f);

        var movement = root.AddComponent<EnemyMovement>();
        movement.agent = agent;

        var enemyAnimator = root.AddComponent<EnemyAnimator>();
        enemyAnimator.animator = animator;
        enemyAnimator.speedParameter = "Speed";

        var sensor = root.AddComponent<EnemySensor>();
        sensor.eyePoint = eye.transform;
        sensor.viewDistance = 12f;
        sensor.viewAngle = 110f;
        sensor.sphereRadius = 0.35f;
        sensor.obstacleMask = 1 << 0; // layer Default: paredes e chão

        var brain = root.AddComponent<EnemyBrain>();
        brain.patrolSpeed = EnemyPatrolSpeed;
        brain.chaseSpeed = EnemyChaseSpeed;
        brain.patrolWaitTime = 2f;
        brain.attackRange = EnemyAttackRange;
        brain.sensor = sensor;
        brain.movement = movement;
        brain.enemyAnimator = enemyAnimator;
        brain.isPatroller = true;
        brain.searchTime = 5f;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject Primitive(PrimitiveType type, string name, Transform parent,
        Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    // ------------------------------------------------------------------ Cena

    static void BuildScene(GameObject playerPrefab, GameObject enemyPrefab, Palette palette)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Hex("8C97A6");
        RenderSettings.fog = false;

        // --- Labirinto
        var maze = new GameObject("Maze");
        int rows = MazeMap.Length, cols = MazeMap[0].Length;

        Primitive(PrimitiveType.Cube, "Floor", maze.transform,
            new Vector3(0f, -0.5f, 0f), new Vector3(cols * CellSize, 1f, rows * CellSize), palette.floor);

        var walls = new GameObject("Walls");
        walls.transform.SetParent(maze.transform, false);

        // Cada sequência horizontal de '#' vira um único bloco.
        for (int r = 0; r < rows; r++)
        {
            int c = 0;
            while (c < cols)
            {
                if (MazeMap[r][c] != '#') { c++; continue; }

                int start = c;
                while (c < cols && MazeMap[r][c] == '#') c++;
                int length = c - start;

                float x = (start + (length - 1) / 2f - (cols - 1) / 2f) * CellSize;
                float z = ((rows - 1) / 2f - r) * CellSize;

                GameObject wall = Primitive(PrimitiveType.Cube, $"Wall_r{r}_c{start}", walls.transform,
                    new Vector3(x, WallHeight / 2f, z),
                    new Vector3(length * CellSize, WallHeight, CellSize), palette.wall);
                GameObjectUtility.SetStaticEditorFlags(wall,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
        }

        // --- NavMesh (só a geometria do labirinto)
        var surface = maze.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;

        AssetDatabase.DeleteAsset(NavMeshAssetPath);
        surface.BuildNavMesh();
        AssetDatabase.CreateAsset(surface.navMeshData, NavMeshAssetPath);

        // --- Jogador
        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
        player.transform.position = TileToWorld(PlayerStart, 1f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);

        // --- Câmera
        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Hex("0E141A");
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 200f;
        cameraObject.AddComponent<AudioListener>();

        // Câmera fixa, de cima, enquadrando o labirinto inteiro. Mova à vontade na cena:
        // ela não se mexe no Play. (Para acompanhar o jogador, use o FollowCamera com Follow ligado.)
        cameraObject.transform.position = new Vector3(0f, 27f, 0f);
        cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // --- Luz
        var lightObject = new GameObject("Directional Light");
        var sun = lightObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Hex("FFF1DC");
        sun.intensity = 1.2f;
        sun.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

        // --- Inimigo
        SpawnEnemy(enemyPrefab, EnemyRoute, player.transform);

        EditorSceneManager.SaveScene(scene, ScenePath);

        // A cena precisa estar importada para a Build Settings guardar o GUID certo.
        AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(AssetDatabase.GUIDFromAssetPath(ScenePath), true),
        };
    }

    static void SpawnEnemy(GameObject prefab, Vector2Int[] route, Transform player)
    {
        var pointsRoot = new GameObject("PatrolPoints");
        var points = new Transform[route.Length];

        for (int i = 0; i < route.Length; i++)
        {
            var p = new GameObject($"Point_{i + 1}");
            p.transform.SetParent(pointsRoot.transform, false);
            p.transform.position = TileToWorld(route[i], 0f);
            points[i] = p.transform;
        }

        var enemy = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        enemy.transform.position = points[0].position;

        Vector3 toNext = points[1].position - points[0].position;
        toNext.y = 0f;
        if (toNext.sqrMagnitude > 0.01f)
            enemy.transform.rotation = Quaternion.LookRotation(toNext);

        var movement = enemy.GetComponent<EnemyMovement>();
        movement.patrolPoints = points;

        var sensor = enemy.GetComponent<EnemySensor>();
        sensor.player = player;

        PrefabUtility.RecordPrefabInstancePropertyModifications(enemy.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(movement);
        PrefabUtility.RecordPrefabInstancePropertyModifications(sensor);
    }

    // ------------------------------------------------------------------ Util

    static Vector3 TileToWorld(Vector2Int tile, float y)
    {
        int rows = MazeMap.Length, cols = MazeMap[0].Length;
        return new Vector3(
            (tile.x - (cols - 1) / 2f) * CellSize,
            y,
            ((rows - 1) / 2f - tile.y) * CellSize);
    }

    // Garante mapa retangular, pontos em corredores e todos os corredores conectados.
    static void ValidateMaze()
    {
        int rows = MazeMap.Length, cols = MazeMap[0].Length;

        foreach (string line in MazeMap)
            if (line.Length != cols)
                throw new Exception("O mapa do labirinto precisa ser retangular.");

        Func<Vector2Int, bool> isFloor = t =>
            t.x >= 0 && t.x < cols && t.y >= 0 && t.y < rows && MazeMap[t.y][t.x] == '.';

        foreach (Vector2Int t in new[] { PlayerStart }.Concat(EnemyRoute))
            if (!isFloor(t))
                throw new Exception($"O ponto {t} não está em um corredor.");

        var seen = new HashSet<Vector2Int> { PlayerStart };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(PlayerStart);

        while (queue.Count > 0)
        {
            Vector2Int cur = queue.Dequeue();
            foreach (Vector2Int d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                Vector2Int next = cur + d;
                if (isFloor(next) && seen.Add(next))
                    queue.Enqueue(next);
            }
        }

        int floorCount = MazeMap.Sum(line => line.Count(ch => ch == '.'));
        if (seen.Count != floorCount)
            throw new Exception($"Há corredores isolados: {seen.Count} alcançáveis de {floorCount}.");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = path.Substring(0, path.LastIndexOf('/'));
        string leaf = path.Substring(path.LastIndexOf('/') + 1);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
