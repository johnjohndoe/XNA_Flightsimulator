using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Storage;


namespace XNAseries2
{
    public class Game1 : Microsoft.Xna.Framework.Game
    {
        enum CollisionType { None, Building, Boundary, Target }

        struct Bullet
        {
            public Vector3 position;
            public Quaternion rotation;
        }

        private struct VertexPointSprite
        {
            private Vector3 position;
            private float pointSize;

            public VertexPointSprite(Vector3 position, float pointSize)
            {
                this.position = position;
                this.pointSize = pointSize;
            }

            public static readonly VertexElement[] VertexElements =
            {
                new VertexElement(0, 0, VertexElementFormat.Vector3, VertexElementMethod.Default, VertexElementUsage.Position, 0),
                new VertexElement(0, sizeof(float)*3, VertexElementFormat.Single, VertexElementMethod.Default, VertexElementUsage.PointSize, 0),
            };
            public static int SizeInBytes = sizeof(float) * (3 + 1);
        }

        GraphicsDeviceManager graphics;
        GraphicsDevice device;

        Effect effect;
        Vector3 lightDirection = new Vector3(3, -2, 5);
        Matrix viewMatrix;
        Matrix projectionMatrix;
        Texture2D sceneryTexture;
        Model xwingModel;
        // Position for the model.
        // Rotate the model by 180 degrees.
        // Note: XNA takes the negative z-axis as forward.
        static Vector3 initialXwingPosition = new Vector3(8, 1, -3);
        Vector3 xwingPosition = initialXwingPosition;
        Quaternion xwingRotation = Quaternion.Identity;
        Quaternion cameraRotation = Quaternion.Identity;
        int[,] floorPlan;
        int[] buildingHeights = new int[] { 0, 2, 2, 6, 5, 4 };

        float gameSpeed = 1.0f;
        const int maxTargets = 50;
        Model targetModel;
        List<BoundingSphere> targetList = new List<BoundingSphere>();

        VertexBuffer cityVertexBuffer;
        VertexDeclaration texturedVertexDeclaration;
        VertexDeclaration pointSpriteVertexDeclaration;

        BoundingBox[] buildingBoundingBoxes;
        BoundingBox completeCityBox;

        Texture2D bulletTexture;
        List<Bullet> bulletList = new List<Bullet>();
        double lastBulletTime = 0;

        Texture2D[] skyboxTextures;
        Model skyboxModel;

        public Game1()
        {
            graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
        }

        protected override void Initialize()
        {
            graphics.PreferredBackBufferWidth = 500;
            graphics.PreferredBackBufferHeight = 500;
            graphics.IsFullScreen = false;
            graphics.ApplyChanges();
            Window.Title = "Riemer's XNA Tutorials -- Series 2 -- Flightsimulator";

            LoadFloorPlan();
            lightDirection.Normalize();
            SetUpBoundingBoxes();
            AddTargets();

            base.Initialize();
        }

        protected override void LoadContent()
        {
            device = graphics.GraphicsDevice;
            pointSpriteVertexDeclaration = new VertexDeclaration(device, VertexPointSprite.VertexElements);
            effect = Content.Load<Effect>("effects");
            sceneryTexture = Content.Load<Texture2D>("texturemap");
            xwingModel = LoadModel("xwing");
            targetModel = LoadModel("target");
            bulletTexture = Content.Load<Texture2D>("bullet");
            skyboxModel = LoadModel("skybox", out skyboxTextures);
            SetUpCamera();
            SetUpVertices();
        }

        private void SetUpCamera()
        {
            viewMatrix = Matrix.CreateLookAt(new Vector3(20, 13, -5), new Vector3(8, 0, -7), new Vector3(0, 1, 0));
            projectionMatrix = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, device.Viewport.AspectRatio, 0.2f, 500.0f);
        }

        private void UpdateCamera()
        {
            // Adjust the camera delay.
            // Each frame the camera rotation gets closer to the X-Wing rotation by 10%.
            cameraRotation = Quaternion.Lerp(cameraRotation, xwingRotation, 0.1f);

            // Camera position relative to the model.
            Vector3 cameraPosition = new Vector3(0, 0.1f, 0.6f);
            // Transform the camera position to correctly align with the model.
            cameraPosition = Vector3.Transform(cameraPosition, Matrix.CreateFromQuaternion(cameraRotation));
            cameraPosition += xwingPosition;

            Vector3 cameraUpVector = new Vector3(0, 1, 0);
            cameraUpVector = Vector3.Transform(cameraUpVector, Matrix.CreateFromQuaternion(cameraRotation));

            viewMatrix = Matrix.CreateLookAt(cameraPosition, xwingPosition, cameraUpVector);
            projectionMatrix = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, device.Viewport.AspectRatio, 0.2f, 500.0f);
        }

        protected override void UnloadContent()
        {
        }

        protected override void Update(GameTime gameTime)
        {
            KeyboardState keyState = Keyboard.GetState();

            // Allows to exit the game.
            if (keyState.IsKeyDown(Keys.Escape))
                this.Exit();

            ProcessKeyboard(gameTime);
            float moveSpeed = gameTime.ElapsedGameTime.Milliseconds / 500.0f * gameSpeed;
            MoveForward(ref xwingPosition, xwingRotation, moveSpeed);

            // Bounding sphere for the model.
            BoundingSphere xwingSpere = new BoundingSphere(xwingPosition, 0.04f);
            // Check for collision.
            if (CheckCollision(xwingSpere) != CollisionType.None)
            {
                // Reset the model position and decrease the speed.
                xwingPosition = initialXwingPosition;
                xwingRotation = Quaternion.Identity;
                gameSpeed /= 1.1f;
            }

            UpdateCamera();
            UpdateBulletPositions(moveSpeed);

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.DarkSlateBlue, 1.0f, 0);

            DrawSkybox();
            DrawCity();
            DrawModel();
            DrawTargets();
            DrawBullets();

            base.Draw(gameTime);
        }

        private void SetUpVertices()
        {
            int differentBuildings = buildingHeights.Length - 1;
            float imagesInTexture = 1 + differentBuildings * 2;

            int cityWidth = floorPlan.GetLength(0);
            int cityLength = floorPlan.GetLength(1);


            List<VertexPositionNormalTexture> verticesList = new List<VertexPositionNormalTexture>();
            for (int x = 0; x < cityWidth; x++)
            {
                for (int z = 0; z < cityLength; z++)
                {
                    int currentbuilding = floorPlan[x, z];

                    // Floor or ceiling.
                    verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, buildingHeights[currentbuilding], -z), new Vector3(0, 1, 0), new Vector2(currentbuilding * 2 / imagesInTexture, 1)));
                    verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, buildingHeights[currentbuilding], -z - 1), new Vector3(0, 1, 0), new Vector2((currentbuilding * 2) / imagesInTexture, 0)));
                    verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, buildingHeights[currentbuilding], -z), new Vector3(0, 1, 0), new Vector2((currentbuilding * 2 + 1) / imagesInTexture, 1)));

                    verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, buildingHeights[currentbuilding], -z - 1), new Vector3(0, 1, 0), new Vector2((currentbuilding * 2) / imagesInTexture, 0)));
                    verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, buildingHeights[currentbuilding], -z - 1), new Vector3(0, 1, 0), new Vector2((currentbuilding * 2 + 1) / imagesInTexture, 0)));
                    verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, buildingHeights[currentbuilding], -z), new Vector3(0, 1, 0), new Vector2((currentbuilding * 2 + 1) / imagesInTexture, 1)));

                    if (currentbuilding != 0)
                    {
                        // Front wall.
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, 0, -z - 1), new Vector3(0, 0, -1), new Vector2((currentbuilding * 2) / imagesInTexture, 1)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, buildingHeights[currentbuilding], -z - 1), new Vector3(0, 0, -1), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 0)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, 0, -z - 1), new Vector3(0, 0, -1), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 1)));

                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, buildingHeights[currentbuilding], -z - 1), new Vector3(0, 0, -1), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 0)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, 0, -z - 1), new Vector3(0, 0, -1), new Vector2((currentbuilding * 2) / imagesInTexture, 1)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, buildingHeights[currentbuilding], -z - 1), new Vector3(0, 0, -1), new Vector2((currentbuilding * 2) / imagesInTexture, 0)));

                        // Back wall.
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, 0, -z), new Vector3(0, 0, 1), new Vector2((currentbuilding * 2) / imagesInTexture, 1)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, 0, -z), new Vector3(0, 0, 1), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 1)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, buildingHeights[currentbuilding], -z), new Vector3(0, 0, 1), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 0)));

                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, buildingHeights[currentbuilding], -z), new Vector3(0, 0, 1), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 0)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, buildingHeights[currentbuilding], -z), new Vector3(0, 0, 1), new Vector2((currentbuilding * 2) / imagesInTexture, 0)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, 0, -z), new Vector3(0, 0, 1), new Vector2((currentbuilding * 2) / imagesInTexture, 1)));

                        // Left wall.
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, 0, -z), new Vector3(-1, 0, 0), new Vector2((currentbuilding * 2) / imagesInTexture, 1)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, 0, -z - 1), new Vector3(-1, 0, 0), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 1)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, buildingHeights[currentbuilding], -z - 1), new Vector3(-1, 0, 0), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 0)));

                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, buildingHeights[currentbuilding], -z - 1), new Vector3(-1, 0, 0), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 0)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, buildingHeights[currentbuilding], -z), new Vector3(-1, 0, 0), new Vector2((currentbuilding * 2) / imagesInTexture, 0)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x, 0, -z), new Vector3(-1, 0, 0), new Vector2((currentbuilding * 2) / imagesInTexture, 1)));

                        // Right wall.
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, 0, -z), new Vector3(1, 0, 0), new Vector2((currentbuilding * 2) / imagesInTexture, 1)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, buildingHeights[currentbuilding], -z - 1), new Vector3(1, 0, 0), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 0)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, 0, -z - 1), new Vector3(1, 0, 0), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 1)));

                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, buildingHeights[currentbuilding], -z - 1), new Vector3(1, 0, 0), new Vector2((currentbuilding * 2 - 1) / imagesInTexture, 0)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, 0, -z), new Vector3(1, 0, 0), new Vector2((currentbuilding * 2) / imagesInTexture, 1)));
                        verticesList.Add(new VertexPositionNormalTexture(new Vector3(x + 1, buildingHeights[currentbuilding], -z), new Vector3(1, 0, 0), new Vector2((currentbuilding * 2) / imagesInTexture, 0)));
                    }
                }
            }


            cityVertexBuffer = new VertexBuffer(device, verticesList.Count * VertexPositionNormalTexture.SizeInBytes, BufferUsage.WriteOnly); cityVertexBuffer.SetData<VertexPositionNormalTexture>(verticesList.ToArray());
            texturedVertexDeclaration = new VertexDeclaration(device, VertexPositionNormalTexture.VertexElements);
        }

        private void LoadFloorPlan()
        {
            // Every 1 indicates a building.
            floorPlan = new int[,]
             {
              {1,1,1,1,1,1,1,1,1,1,1,1,1,1,1},
              {1,0,0,0,0,0,0,0,0,0,0,0,0,0,1},
              {1,0,0,1,1,0,0,0,1,1,0,0,1,0,1},
              {1,0,0,1,1,0,0,0,1,0,0,0,1,0,1},
              {1,0,0,0,1,1,0,1,1,0,0,0,0,0,1},
              {1,0,0,0,0,0,0,0,0,0,0,1,0,0,1},
              {1,0,0,0,0,0,0,0,0,0,0,0,0,0,1},
              {1,0,0,0,0,0,0,0,0,0,0,0,0,0,1},
              {1,0,0,0,0,0,0,0,0,0,0,0,0,0,1},
              {1,0,0,0,0,0,0,0,0,0,0,0,0,0,1},
              {1,0,1,1,0,0,0,1,0,0,0,0,0,0,1},
              {1,0,1,0,0,0,0,0,0,0,0,0,0,0,1},
              {1,0,0,0,0,0,0,0,0,0,0,0,0,0,1},
              {1,0,0,0,0,0,0,0,0,0,0,0,0,0,1},
              {1,0,0,0,0,1,0,0,0,0,0,0,0,0,1},
              {1,0,0,0,0,1,0,0,0,1,0,0,0,0,1},
              {1,0,1,0,0,0,0,0,0,1,0,0,0,0,1},
              {1,0,1,1,0,0,0,0,1,1,0,0,0,1,1},
              {1,0,0,0,0,0,0,0,1,1,0,0,0,1,1},
              {1,1,1,1,1,1,1,1,1,1,1,1,1,1,1},
             };
            // Adding random heights to the buildings.
            Random random = new Random();
            int differentBuildings = buildingHeights.Length - 1;
            for (int x = 0; x < floorPlan.GetLength(0); x++)
                for (int y = 0; y < floorPlan.GetLength(1); y++)
                    if (floorPlan[x, y] == 1)
                        floorPlan[x, y] = random.Next(differentBuildings) + 1;
        }

        private void DrawCity()
        {
            effect.CurrentTechnique = effect.Techniques["Textured"];
            effect.Parameters["xWorld"].SetValue(Matrix.Identity);
            effect.Parameters["xView"].SetValue(viewMatrix);
            effect.Parameters["xProjection"].SetValue(projectionMatrix);
            effect.Parameters["xTexture"].SetValue(sceneryTexture);
            effect.Parameters["xEnableLighting"].SetValue(true);
            effect.Parameters["xLightDirection"].SetValue(lightDirection);
            effect.Parameters["xAmbient"].SetValue(0.5f);
            effect.Begin();
            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Begin();
                device.VertexDeclaration = texturedVertexDeclaration;
                device.Vertices[0].SetSource(cityVertexBuffer, 0, VertexPositionNormalTexture.SizeInBytes);
                device.DrawPrimitives(PrimitiveType.TriangleList, 0, cityVertexBuffer.SizeInBytes / VertexPositionNormalTexture.SizeInBytes / 3);
                pass.End();
            }
            effect.End();
        }

        private Model LoadModel(string assetName)
        {
            Model newModel = Content.Load<Model>(assetName);
            // Initialize the model by applying the effect.
            foreach (ModelMesh mesh in newModel.Meshes)
            {
                foreach (ModelMeshPart meshPart in mesh.MeshParts)
                {
                    meshPart.Effect = effect.Clone(device);
                }
            }
            return newModel;
        }

        private void DrawModel()
        {
            // Create world matrix to render the model with the following properties.
            // 1) Scale the model.
            // 2) Rotate the model by 180 degrees.
            // Note: XNA takes the negative z-axis as forward.
            // 3) Rotate the model as defined by the quaternion.
            // 4) Translate the model.
            Matrix worldMatrix = Matrix.CreateScale(0.0005f, 0.0005f, 0.0005f) * Matrix.CreateRotationY(MathHelper.Pi) * Matrix.CreateFromQuaternion(xwingRotation) * Matrix.CreateTranslation(xwingPosition);

            // Render the model parts at their correct positions (based on the bones matrices).
            Matrix[] xwingTransforms = new Matrix[xwingModel.Bones.Count];
            xwingModel.CopyAbsoluteBoneTransformsTo(xwingTransforms);
            foreach (ModelMesh mesh in xwingModel.Meshes)
            {
                foreach (Effect currentEffect in mesh.Effects)
                {
                    currentEffect.CurrentTechnique = currentEffect.Techniques["Colored"];
                    currentEffect.Parameters["xWorld"].SetValue(xwingTransforms[mesh.ParentBone.Index] * worldMatrix);
                    currentEffect.Parameters["xView"].SetValue(viewMatrix);
                    currentEffect.Parameters["xProjection"].SetValue(projectionMatrix);
                    currentEffect.Parameters["xEnableLighting"].SetValue(true);
                    currentEffect.Parameters["xLightDirection"].SetValue(lightDirection);
                    currentEffect.Parameters["xAmbient"].SetValue(0.5f);
                }
                mesh.Draw();
            }
        }

        private void ProcessKeyboard(GameTime gameTime)
        {
            KeyboardState keys = Keyboard.GetState();

            // Prepare the steering rotation for the model.
            float leftRightRotation = 0;

            float turningSpeed = (float)gameTime.ElapsedGameTime.TotalMilliseconds / 1000.0f;
            turningSpeed *= 1.6f * gameSpeed;
            if (keys.IsKeyDown(Keys.Right))
                leftRightRotation += turningSpeed;
            if (keys.IsKeyDown(Keys.Left))
                leftRightRotation -= turningSpeed;
            float upDownRotation = 0;
            if (keys.IsKeyDown(Keys.Down))
                upDownRotation += turningSpeed;
            if (keys.IsKeyDown(Keys.Up))
                upDownRotation -= turningSpeed;

            // Update current model rotation with the steering rotation.
            Quaternion additionalRotation = Quaternion.CreateFromAxisAngle(new Vector3(0, 0, -1), leftRightRotation) * Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), upDownRotation);
            xwingRotation *= additionalRotation;

            // Generate bullets.
            if (keys.IsKeyDown(Keys.Space))
            {
                double currentTime = gameTime.TotalGameTime.TotalMilliseconds;
                if (currentTime - lastBulletTime > 100)
                {
                    Bullet newBullet = new Bullet();
                    newBullet.position = xwingPosition;
                    newBullet.rotation = xwingRotation;
                    bulletList.Add(newBullet);

                    lastBulletTime = currentTime;
                }
            }
        }

        private void MoveForward(ref Vector3 position, Quaternion rotationQuat, float speed)
        {
            Vector3 addVector = Vector3.Transform(new Vector3(0, 0, -1), rotationQuat);
            position += addVector * speed;
        }

        private void SetUpBoundingBoxes()
        {
            int cityWidth = floorPlan.GetLength(0);
            int cityLength = floorPlan.GetLength(1);

            // Individual bounding boxes for each building.
            List<BoundingBox> bbList = new List<BoundingBox>();
            for (int x = 0; x < cityWidth; x++)
            {
                for (int z = 0; z < cityLength; z++)
                {
                    int buildingType = floorPlan[x, z];
                    if (buildingType != 0)
                    {
                        int buildingHeight = buildingHeights[buildingType];
                        Vector3[] buildingPoints = new Vector3[2];
                        buildingPoints[0] = new Vector3(x, 0, -z); // The lower-back-left point of the building box.
                        buildingPoints[1] = new Vector3(x + 1, buildingHeight, -z - 1); // The upper-front-right point of the building box.
                        BoundingBox buildingBox = BoundingBox.CreateFromPoints(buildingPoints);
                        bbList.Add(buildingBox);
                    }
                }
            }
            buildingBoundingBoxes = bbList.ToArray();

            // Bounding box for the whole city.
            Vector3[] boundaryPoints = new Vector3[2];
            boundaryPoints[0] = new Vector3(0, 0, 0);
            boundaryPoints[1] = new Vector3(cityWidth, 20, -cityLength);
            completeCityBox = BoundingBox.CreateFromPoints(boundaryPoints);
        }

        private CollisionType CheckCollision(BoundingSphere sphere)
        {
            // Buildings.
            for (int i = 0; i < buildingBoundingBoxes.Length; i++)
                if (buildingBoundingBoxes[i].Contains(sphere) != ContainmentType.Disjoint)
                    return CollisionType.Building;

            // City.
            if (completeCityBox.Contains(sphere) != ContainmentType.Contains)
                return CollisionType.Boundary;

            // Targets.
            for (int i = 0; i < targetList.Count; i++)
            {
                if (targetList[i].Contains(sphere) != ContainmentType.Disjoint)
                {
                    targetList.RemoveAt(i);
                    i--;
                    AddTargets();

                    return CollisionType.Target;
                }
            }

            return CollisionType.None;
        }

        private void AddTargets()
        {
            int cityWidth = floorPlan.GetLength(0);
            int cityLength = floorPlan.GetLength(1);

            Random random = new Random();

            while (targetList.Count < maxTargets)
            {
                int x = random.Next(cityWidth);
                int z = -random.Next(cityLength);
                float y = (float)random.Next(2000) / 1000f + 1;
                float radius = (float)random.Next(1000) / 1000f * 0.2f + 0.01f;

                BoundingSphere newTarget = new BoundingSphere(new Vector3(x, y, z), radius);

                if (CheckCollision(newTarget) == CollisionType.None)
                    targetList.Add(newTarget);
            }
        }

        private void DrawTargets()
        {
            for (int i = 0; i < targetList.Count; i++)
            {
                Matrix worldMatrix = Matrix.CreateScale(targetList[i].Radius) * Matrix.CreateTranslation(targetList[i].Center);

                Matrix[] targetTransforms = new Matrix[targetModel.Bones.Count];
                targetModel.CopyAbsoluteBoneTransformsTo(targetTransforms);
                foreach (ModelMesh modelMesh in targetModel.Meshes)
                {
                    foreach (Effect currentEffect in modelMesh.Effects)
                    {
                        currentEffect.CurrentTechnique = currentEffect.Techniques["Colored"];
                        currentEffect.Parameters["xWorld"].SetValue(targetTransforms[modelMesh.ParentBone.Index] * worldMatrix);
                        currentEffect.Parameters["xView"].SetValue(viewMatrix);
                        currentEffect.Parameters["xProjection"].SetValue(projectionMatrix);
                        currentEffect.Parameters["xEnableLighting"].SetValue(true);
                        currentEffect.Parameters["xLightDirection"].SetValue(lightDirection);
                        currentEffect.Parameters["xAmbient"].SetValue(0.5f);
                    }
                    modelMesh.Draw();
                }
            }
        }

        private void UpdateBulletPositions(float moveSpeed)
        {
            for (int i = 0; i < bulletList.Count; i++)
            {
                Bullet currentBullet = bulletList[i];
                // The speed is set relative to the speed of the X-Wing.
                MoveForward(ref currentBullet.position, currentBullet.rotation, moveSpeed * 2.0f);
                bulletList[i] = currentBullet;

                // Check if bullets hit target.
                BoundingSphere bulletSphere = new BoundingSphere(currentBullet.position, 0.05f);
                CollisionType collisionType = CheckCollision(bulletSphere);
                if (collisionType != CollisionType.None)
                {
                    bulletList.RemoveAt(i);
                    i--;

                    if (collisionType == CollisionType.Target)
                        gameSpeed *= 1.05f;
                }
            }
        }

        private void DrawBullets()
        {
            if (bulletList.Count > 0)
            {
                VertexPointSprite[] spriteArray = new VertexPointSprite[bulletList.Count];
                for (int i = 0; i < bulletList.Count; i++)
                    spriteArray[i] = new VertexPointSprite(bulletList[i].position, 50);

                effect.CurrentTechnique = effect.Techniques["PointSprites"];
                Matrix worldMatrix = Matrix.Identity;
                effect.Parameters["xWorld"].SetValue(worldMatrix);
                effect.Parameters["xView"].SetValue(viewMatrix);
                effect.Parameters["xProjection"].SetValue(projectionMatrix);
                effect.Parameters["xTexture"].SetValue(bulletTexture);
                device.RenderState.AlphaBlendEnable = true;
                device.RenderState.SourceBlend = Blend.One;
                device.RenderState.DestinationBlend = Blend.One;

                device.RenderState.PointSpriteEnable = true;

                effect.Begin();
                foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                {
                    pass.Begin();
                    device.VertexDeclaration = pointSpriteVertexDeclaration;
                    device.DrawUserPrimitives(PrimitiveType.PointList, spriteArray, 0, spriteArray.Length);
                    pass.End();
                }
                effect.End();

                device.RenderState.PointSpriteEnable = false;
                device.RenderState.AlphaBlendEnable = false;
            }
        }

        private Model LoadModel(string assetName, out Texture2D[] textures)
        {

            Model newModel = Content.Load<Model>(assetName);
            textures = new Texture2D[newModel.Meshes.Count];
            int i = 0;
            foreach (ModelMesh meshModel in newModel.Meshes)
                foreach (BasicEffect currentEffect in meshModel.Effects)
                    textures[i++] = currentEffect.Texture;

            foreach (ModelMesh mesh in newModel.Meshes)
                foreach (ModelMeshPart meshPart in mesh.MeshParts)
                    meshPart.Effect = effect.Clone(device);

            return newModel;
        }

        private void DrawSkybox()
        {
            device.SamplerStates[0].AddressU = TextureAddressMode.Clamp;
            device.SamplerStates[0].AddressV = TextureAddressMode.Clamp;

            device.RenderState.DepthBufferWriteEnable = false;
            Matrix[] skyboxTransforms = new Matrix[skyboxModel.Bones.Count];
            skyboxModel.CopyAbsoluteBoneTransformsTo(skyboxTransforms);
            int i = 0;
            foreach (ModelMesh meshModel in skyboxModel.Meshes)
            {
                foreach (Effect currentEffect in meshModel.Effects)
                {
                    Matrix worldMatrix = skyboxTransforms[meshModel.ParentBone.Index] * Matrix.CreateTranslation(xwingPosition);
                    currentEffect.CurrentTechnique = currentEffect.Techniques["Textured"];
                    currentEffect.Parameters["xWorld"].SetValue(worldMatrix);
                    currentEffect.Parameters["xView"].SetValue(viewMatrix);
                    currentEffect.Parameters["xProjection"].SetValue(projectionMatrix);
                    currentEffect.Parameters["xTexture"].SetValue(skyboxTextures[i++]);
                }
                meshModel.Draw();
            }
            device.RenderState.DepthBufferWriteEnable = true;
        }

    }
}