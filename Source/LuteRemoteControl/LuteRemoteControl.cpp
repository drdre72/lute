#include "LuteRemoteControl.h"
#include "HttpServerModule.h"
#include "IHttpRouter.h"
#include "HttpPath.h"
#include "HttpServerRequest.h"
#include "HttpServerResponse.h"
#include "HttpServerHttpVersion.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "Engine/World.h"
#include "Engine/Engine.h"
#include "EngineUtils.h"
#include "GameFramework/Actor.h"
#include "GameFramework/GameModeBase.h"
#include "GameFramework/PlayerStart.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/PlayerController.h"
#include "Tickable.h"
#include "Kismet/GameplayStatics.h"
#include "Kismet/KismetSystemLibrary.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "ImageUtils.h"
#include "Engine/TextureRenderTarget2D.h"
#include "Components/SceneCaptureComponent2D.h"
#include "Camera/CameraComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Components/SceneComponent.h"
#include "Engine/StaticMesh.h"
#include "Engine/StaticMeshActor.h"
#include "Engine/ObjectLibrary.h"
#include "UObject/UObjectGlobals.h"
#include "EngineUtils.h"
#include "Landscape.h"
#include "LandscapeInfo.h"
#include "LandscapeComponent.h"
#include "LandscapeDataAccess.h"
#include "LandscapeEdit.h"
#include "LandscapeEditLayer.h"
#include "LandscapeStreamingProxy.h"
#include "LandscapeLayerInfoObject.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Materials/MaterialInterface.h"
#include "Materials/Material.h"
#include "Materials/MaterialExpressionConstant3Vector.h"
#include "Materials/MaterialExpressionLandscapeLayerBlend.h"
#include "Materials/MaterialExpressionLandscapeLayerCoords.h"
#include "Misc/Base64.h"
#include "Components/HierarchicalInstancedStaticMeshComponent.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Engine/Texture.h"
#include "Engine/Texture2D.h"
#include "Materials/MaterialExpressionTextureSampleParameter2D.h"
#include "WorldPartition/WorldPartition.h"
#include "WorldPartition/ActorDescContainerInstance.h"

DEFINE_LOG_CATEGORY_STATIC(LogLuteRC, Log, All);

#define LUTE_LOG(Msg, ...) UE_LOG(LogLuteRC, Log, TEXT("[LuteRC] " Msg), ##__VA_ARGS__)

using FResponseCallback = TFunction<void(TUniquePtr<FHttpServerResponse>&&)>;

// Helper: create JSON response
static TUniquePtr<FHttpServerResponse> MakeJsonResponse(const FString& JsonStr, EHttpServerResponseCodes Code = EHttpServerResponseCodes::Ok)
{
    auto Response = FHttpServerResponse::Create(JsonStr, TEXT("application/json"));
    Response->Code = Code;
    return Response;
}

// Helper: serialize JSON object to string
static FString JsonToString(const TSharedPtr<FJsonObject>& JsonObj)
{
    FString OutputStr;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutputStr);
    FJsonSerializer::Serialize(JsonObj.ToSharedRef(), Writer);
    return OutputStr;
}

// Helper: parse JSON from request body
static TSharedPtr<FJsonObject> ParseJsonBody(const FHttpServerRequest& Request)
{
    TArray<uint8> BodyCopy = Request.Body;
    BodyCopy.Add(0); // null terminate
    FString BodyStr = FString(UTF8_TO_TCHAR(reinterpret_cast<const char*>(BodyCopy.GetData())));
    TSharedPtr<FJsonObject> JsonObj;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(BodyStr);
    FJsonSerializer::Deserialize(Reader, JsonObj);
    return JsonObj;
}

// Get the editor world
static UWorld* GetActiveWorld()
{
    if (GEditor)
    {
        FWorldContext& EditorContext = GEditor->GetEditorWorldContext();
        if (EditorContext.World())
        {
            return EditorContext.World();
        }
    }
    if (GEngine)
    {
        for (const FWorldContext& Context : GEngine->GetWorldContexts())
        {
            if (Context.WorldType == EWorldType::Editor || Context.WorldType == EWorldType::PIE)
            {
                return Context.World();
            }
        }
    }
    return nullptr;
}

// Handle: GET /state — world state query
static bool HandleGetState(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();
    UWorld* World = GetActiveWorld();

    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    int32 ActorCount = 0;
    TArray<TSharedPtr<FJsonValue>> SampleActors;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        ActorCount++;
        if (SampleActors.Num() < 20)
        {
            SampleActors.Add(MakeShared<FJsonValueString>(It->GetName()));
        }
    }

    Json->SetNumberField("actor_count", ActorCount);
    Json->SetStringField("map_name", World->GetMapName());
    Json->SetArrayField("sample_actors", SampleActors);

    LUTE_LOG("State: %d actors in %s", ActorCount, *World->GetMapName());
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /command — run a console command
static bool HandleCommand(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    FString Cmd = Body.IsValid() ? Body->GetStringField(TEXT("command")) : TEXT("");

    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (Cmd.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'command' field");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (World)
    {
        APlayerController* PC = World->GetFirstPlayerController();
        if (PC)
        {
            FString Output = PC->ConsoleCommand(Cmd, true);
            Json->SetStringField("output", Output);
            Json->SetBoolField("success", true);
            LUTE_LOG("Command: %s -> %s", *Cmd, *Output);
        }
        else
        {
            Json->SetStringField("error", "No player controller");
            Json->SetBoolField("success", false);
        }
    }
    else
    {
        Json->SetStringField("error", "No active world");
        Json->SetBoolField("success", false);
    }

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /spawn — spawn an actor by class path
static bool HandleSpawn(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString ClassPath = Body->GetStringField(TEXT("class_path"));
    double X = Body->GetNumberField(TEXT("x"));
    double Y = Body->GetNumberField(TEXT("y"));
    double Z = Body->GetNumberField(TEXT("z"));

    if (ClassPath.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'class_path' field");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    UClass* ActorClass = LoadClass<AActor>(nullptr, *ClassPath);
    if (!ActorClass)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Failed to load class: %s"), *ClassPath));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FActorSpawnParameters SpawnParams;
    AActor* SpawnedActor = World->SpawnActor<AActor>(ActorClass, FVector(X, Y, Z), FRotator::ZeroRotator, SpawnParams);

    if (SpawnedActor)
    {
        Json->SetBoolField("success", true);
        Json->SetStringField("actor_name", SpawnedActor->GetName());
        LUTE_LOG("Spawned %s at (%.1f, %.1f, %.1f)", *SpawnedActor->GetName(), X, Y, Z);
    }
    else
    {
        Json->SetStringField("error", "Failed to spawn actor");
        Json->SetBoolField("success", false);
    }

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: GET /screenshot — capture a screenshot and return Base64 JPEG
static bool HandleScreenshot(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Capture screenshot to file
    FString ScreenshotPath = FPaths::ProjectSavedDir() / TEXT("Screenshots") / TEXT("lute_rc.jpg");
    FString Cmd = FString::Printf(TEXT("shot %s"), *ScreenshotPath);

    APlayerController* PC = World->GetFirstPlayerController();
    if (PC)
    {
        FString Output = PC->ConsoleCommand(Cmd, true);

        // Read the file and encode as Base64
        TArray<uint8> FileData;
        if (FFileHelper::LoadFileToArray(FileData, *ScreenshotPath))
        {
            FString Base64 = FBase64::Encode(FileData);
            Json->SetBoolField("success", true);
            Json->SetStringField("path", ScreenshotPath);
            Json->SetStringField("base64", FString::Printf(TEXT("data:image/jpeg;base64,%s"), *Base64));
            Json->SetNumberField("size", FileData.Num());
            LUTE_LOG("Screenshot saved + base64 (%d bytes raw, %d chars b64)", FileData.Num(), Base64.Len());
        }
        else
        {
            // File not ready yet — return path only
            Json->SetBoolField("success", true);
            Json->SetStringField("path", ScreenshotPath);
            Json->SetStringField("base64", "");
            LUTE_LOG("Screenshot saved to %s (base64 pending)", *ScreenshotPath);
        }
    }
    else
    {
        Json->SetStringField("error", "No player controller");
        Json->SetBoolField("success", false);
    }

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /set_mesh — set skeletal mesh on an actor by name
static bool HandleSetMesh(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString ActorName = Body->GetStringField(TEXT("actor_name"));
    FString MeshPath = Body->GetStringField(TEXT("mesh_path"));

    if (ActorName.IsEmpty() || MeshPath.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'actor_name' or 'mesh_path'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    AActor* TargetActor = nullptr;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetName() == ActorName)
        {
            TargetActor = *It;
            break;
        }
    }

    if (!TargetActor)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Actor not found: %s"), *ActorName));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    USkeletalMesh* SkeletalMesh = LoadObject<USkeletalMesh>(nullptr, *MeshPath);
    if (!SkeletalMesh)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Failed to load mesh: %s"), *MeshPath));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    USkeletalMeshComponent* MeshComp = TargetActor->FindComponentByClass<USkeletalMeshComponent>();
    if (!MeshComp)
    {
        // Try to find the default mesh component on a character
        MeshComp = Cast<USkeletalMeshComponent>(TargetActor->GetComponentByClass(USkeletalMeshComponent::StaticClass()));
    }

    if (MeshComp)
    {
        MeshComp->SetSkeletalMesh(SkeletalMesh);
        Json->SetBoolField("success", true);
        Json->SetStringField("actor", TargetActor->GetName());
        LUTE_LOG("Set mesh %s on %s", *MeshPath, *TargetActor->GetName());
    }
    else
    {
        Json->SetStringField("error", "Actor has no SkeletalMeshComponent");
        Json->SetBoolField("success", false);
    }

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: GET /list_meshes — list all mesh component names on an actor
static bool HandleListMeshes(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    // Parse actor_name from body
    FString ActorName;
    TArray<uint8> BodyCopy = Request.Body;
    BodyCopy.Add(0);
    FString BodyStr = FString(UTF8_TO_TCHAR(reinterpret_cast<const char*>(BodyCopy.GetData())));
    if (!BodyStr.IsEmpty())
    {
        TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
        if (Body.IsValid())
        {
            ActorName = Body->GetStringField(TEXT("actor_name"));
        }
    }

    if (ActorName.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'actor_name'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    AActor* TargetActor = nullptr;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetName() == ActorName)
        {
            TargetActor = *It;
            break;
        }
    }

    if (!TargetActor)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Actor not found: %s"), *ActorName));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    TArray<TSharedPtr<FJsonValue>> MeshList;
    TArray<USkeletalMeshComponent*> SkeletalMeshes;
    TargetActor->GetComponents<USkeletalMeshComponent>(SkeletalMeshes);
    for (USkeletalMeshComponent* MeshComp : SkeletalMeshes)
    {
        TSharedPtr<FJsonObject> MeshInfo = MakeShared<FJsonObject>();
        MeshInfo->SetStringField("name", MeshComp->GetName());
        MeshInfo->SetStringField("type", "SkeletalMesh");
        MeshInfo->SetBoolField("visible", MeshComp->IsVisible());
        if (MeshComp->GetSkeletalMeshAsset())
        {
            MeshInfo->SetStringField("mesh_asset", MeshComp->GetSkeletalMeshAsset()->GetName());
        }
        MeshList.Add(MakeShared<FJsonValueObject>(MeshInfo));
    }

    TArray<UStaticMeshComponent*> StaticMeshes;
    TargetActor->GetComponents<UStaticMeshComponent>(StaticMeshes);
    for (UStaticMeshComponent* MeshComp : StaticMeshes)
    {
        TSharedPtr<FJsonObject> MeshInfo = MakeShared<FJsonObject>();
        MeshInfo->SetStringField("name", MeshComp->GetName());
        MeshInfo->SetStringField("type", "StaticMesh");
        MeshInfo->SetBoolField("visible", MeshComp->IsVisible());
        MeshList.Add(MakeShared<FJsonValueObject>(MeshInfo));
    }

    Json->SetArrayField("meshes", MeshList);
    Json->SetStringField("actor", TargetActor->GetName());

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /list_materials — list all material slots on an actor's skeletal mesh
static bool HandleListMaterials(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString ActorName = Body->GetStringField(TEXT("actor_name"));
    if (ActorName.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'actor_name'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    AActor* TargetActor = nullptr;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetName() == ActorName)
        {
            TargetActor = *It;
            break;
        }
    }

    if (!TargetActor)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Actor not found: %s"), *ActorName));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    TArray<TSharedPtr<FJsonValue>> MaterialList;
    TArray<USkeletalMeshComponent*> SkeletalMeshes;
    TargetActor->GetComponents<USkeletalMeshComponent>(SkeletalMeshes);
    for (USkeletalMeshComponent* MeshComp : SkeletalMeshes)
    {
        int32 NumMaterials = MeshComp->GetNumMaterials();
        for (int32 i = 0; i < NumMaterials; i++)
        {
            TSharedPtr<FJsonObject> MatInfo = MakeShared<FJsonObject>();
            MatInfo->SetNumberField("slot_index", i);
            UMaterialInterface* Mat = MeshComp->GetMaterial(i);
            MatInfo->SetStringField("material_name", Mat ? Mat->GetName() : TEXT("None"));
            MatInfo->SetStringField("material_path", Mat ? Mat->GetPathName() : TEXT(""));
            MatInfo->SetStringField("mesh_component", MeshComp->GetName());
            MaterialList.Add(MakeShared<FJsonValueObject>(MatInfo));
        }
    }

    Json->SetArrayField("materials", MaterialList);
    Json->SetStringField("actor", TargetActor->GetName());

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /toggle_material — hide/show a material slot by setting it to transparent or restoring it
static bool HandleToggleMaterial(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString ActorName = Body->GetStringField(TEXT("actor_name"));
    FString MaterialName = Body->GetStringField(TEXT("material_name"));
    bool bVisible = Body->GetBoolField(TEXT("visible"));
    int32 SlotIndex = Body->GetNumberField(TEXT("slot_index"));

    if (ActorName.IsEmpty() || (MaterialName.IsEmpty() && SlotIndex < 0))
    {
        Json->SetStringField("error", "Missing 'actor_name' and either 'material_name' or 'slot_index'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    AActor* TargetActor = nullptr;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetName() == ActorName)
        {
            TargetActor = *It;
            break;
        }
    }

    if (!TargetActor)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Actor not found: %s"), *ActorName));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    int32 ToggledCount = 0;
    TArray<TSharedPtr<FJsonValue>> ToggledList;
    TArray<USkeletalMeshComponent*> SkeletalMeshes;
    TargetActor->GetComponents<USkeletalMeshComponent>(SkeletalMeshes);
    for (USkeletalMeshComponent* MeshComp : SkeletalMeshes)
    {
        int32 NumMaterials = MeshComp->GetNumMaterials();
        for (int32 i = 0; i < NumMaterials; i++)
        {
            UMaterialInterface* Mat = MeshComp->GetMaterial(i);
            bool bMatch = false;
            if (SlotIndex >= 0)
            {
                bMatch = (i == SlotIndex);
            }
            else if (Mat)
            {
                bMatch = Mat->GetName().Contains(MaterialName);
            }

            if (bMatch)
            {
                if (bVisible)
                {
                    // Restore: re-run setup_fina_materials logic for this slot
                    // Try to find the original material name from the skeletal mesh asset
                    if (USkeletalMesh* SkelMesh = MeshComp->GetSkeletalMeshAsset())
                    {
                        if (i < SkelMesh->GetMaterials().Num())
                        {
                            UMaterialInterface* OriginalMat = SkelMesh->GetMaterials()[i].MaterialInterface;
                            if (OriginalMat)
                            {
                                // Create a new MID from the original material with texture
                                static UMaterial* SimpleTexturedMat2 = nullptr;
                                if (!SimpleTexturedMat2)
                                {
                                    SimpleTexturedMat2 = NewObject<UMaterial>(GetTransientPackage(), TEXT("SimpleTexturedMat2"), RF_Transient);
                                    if (SimpleTexturedMat2)
                                    {
                                        UMaterialExpressionTextureSampleParameter2D* TexSample = NewObject<UMaterialExpressionTextureSampleParameter2D>(SimpleTexturedMat2);
                                        TexSample->ParameterName = TEXT("BaseColorTexture");
                                        TexSample->SamplerType = SAMPLERTYPE_Color;
                                        SimpleTexturedMat2->GetExpressionCollection().AddExpression(TexSample);
                                        UMaterialEditorOnlyData* EditorData = SimpleTexturedMat2->GetEditorOnlyData();
                                        if (EditorData)
                                        {
                                            EditorData->BaseColor.Expression = TexSample;
                                            EditorData->BaseColor.OutputIndex = 0;
                                        }
                                        SimpleTexturedMat2->PreEditChange(nullptr);
                                        SimpleTexturedMat2->PostEditChange();
                                    }
                                }

                                // Map material name to texture
                                FString OrigName = OriginalMat->GetName();
                                FString TexPath;
                                if (OrigName == TEXT("Body")) TexPath = TEXT("/Game/Characters/Fina/Textures/Body_BaseColor.Body_BaseColor");
                                else if (OrigName == TEXT("Face")) TexPath = TEXT("/Game/Characters/Fina/Textures/Face_BaseColor.Face_BaseColor");
                                else if (OrigName == TEXT("Hair")) TexPath = TEXT("/Game/Characters/Fina/Textures/Hair_BaseColor.Hair_BaseColor");
                                else if (OrigName == TEXT("eye")) TexPath = TEXT("/Game/Characters/Fina/Textures/eye_BaseColor.eye_BaseColor");
                                else if (OrigName == TEXT("Maunt")) TexPath = TEXT("/Game/Characters/Fina/Textures/Mount_Base_Color.Mount_Base_Color");
                                else if (OrigName == TEXT("clother")) TexPath = TEXT("/Game/Characters/Fina/Textures/clothes_BaseColor.clothes_BaseColor");

                                if (!TexPath.IsEmpty() && SimpleTexturedMat2)
                                {
                                    UTexture2D* Tex = LoadObject<UTexture2D>(nullptr, *TexPath);
                                    if (Tex)
                                    {
                                        UMaterialInstanceDynamic* DynMat = UMaterialInstanceDynamic::Create(SimpleTexturedMat2, MeshComp);
                                        if (DynMat)
                                        {
                                            DynMat->SetTextureParameterValue(TEXT("BaseColorTexture"), Tex);
                                            MeshComp->SetMaterial(i, DynMat);
                                        }
                                    }
                                }
                                else
                                {
                                    MeshComp->SetMaterial(i, OriginalMat);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Hide by setting to a truly transparent material
                    static UMaterial* TransparentMat = nullptr;
                    if (!TransparentMat)
                    {
                        TransparentMat = NewObject<UMaterial>(GetTransientPackage(), TEXT("TransparentHideMat"), RF_Transient);
                        if (TransparentMat)
                        {
                            TransparentMat->BlendMode = EBlendMode::BLEND_Translucent;
                            TransparentMat->bUsedWithSkeletalMesh = true;
                            UMaterialEditorOnlyData* EditorData = TransparentMat->GetEditorOnlyData();
                            if (EditorData)
                            {
                                // Set Opacity to 0 so the material is fully transparent
                                EditorData->Opacity.UseConstant = 1;
                                EditorData->Opacity.Constant = 0.0f;
                            }
                            TransparentMat->PreEditChange(nullptr);
                            TransparentMat->PostEditChange();
                        }
                    }
                    if (TransparentMat)
                    {
                        MeshComp->SetMaterial(i, TransparentMat);
                    }
                }

                TSharedPtr<FJsonObject> Info = MakeShared<FJsonObject>();
                Info->SetNumberField("slot_index", i);
                Info->SetStringField("material_name", Mat ? Mat->GetName() : TEXT("None"));
                Info->SetStringField("mesh_component", MeshComp->GetName());
                Info->SetBoolField("visible", bVisible);
                ToggledList.Add(MakeShared<FJsonValueObject>(Info));
                ToggledCount++;
            }
        }
    }

    Json->SetBoolField("success", ToggledCount > 0);
    Json->SetNumberField("toggled_count", ToggledCount);
    Json->SetArrayField("toggled", ToggledList);
    Json->SetStringField("actor", TargetActor->GetName());

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /setup_fina_materials — create textured materials for Fina's body parts
static bool HandleSetupFinaMaterials(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString ActorName = Body->GetStringField(TEXT("actor_name"));
    if (ActorName.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'actor_name'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    AActor* TargetActor = nullptr;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetName() == ActorName)
        {
            TargetActor = *It;
            break;
        }
    }

    if (!TargetActor)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Actor not found: %s"), *ActorName));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Map material slot names to texture paths
    TMap<FString, FString> TextureMap;
    TextureMap.Add(TEXT("Body"), TEXT("/Game/Characters/Fina/Textures/Body_BaseColor.Body_BaseColor"));
    TextureMap.Add(TEXT("Face"), TEXT("/Game/Characters/Fina/Textures/Face_BaseColor.Face_BaseColor"));
    TextureMap.Add(TEXT("Hair"), TEXT("/Game/Characters/Fina/Textures/Hair_BaseColor.Hair_BaseColor"));
    TextureMap.Add(TEXT("eye"), TEXT("/Game/Characters/Fina/Textures/eye_BaseColor.eye_BaseColor"));
    TextureMap.Add(TEXT("Maunt"), TEXT("/Game/Characters/Fina/Textures/Mount_Base_Color.Mount_Base_Color"));
    TextureMap.Add(TEXT("clother"), TEXT("/Game/Characters/Fina/Textures/clothes_BaseColor.clothes_BaseColor"));

    // Create a simple parent material with a texture parameter
    static UMaterial* SimpleTexturedMat = nullptr;
    if (!SimpleTexturedMat)
    {
        SimpleTexturedMat = NewObject<UMaterial>(GetTransientPackage(), TEXT("SimpleTexturedMat"), RF_Transient);
        if (SimpleTexturedMat)
        {
            UMaterialExpressionTextureSampleParameter2D* TexSample = NewObject<UMaterialExpressionTextureSampleParameter2D>(SimpleTexturedMat);
            TexSample->ParameterName = TEXT("BaseColorTexture");
            TexSample->SamplerType = SAMPLERTYPE_Color;
            SimpleTexturedMat->GetExpressionCollection().AddExpression(TexSample);
            
            // Connect texture sample RGB output to BaseColor input
            UMaterialEditorOnlyData* EditorData = SimpleTexturedMat->GetEditorOnlyData();
            if (EditorData)
            {
                EditorData->BaseColor.Expression = TexSample;
                EditorData->BaseColor.OutputIndex = 0;
            }
            
            SimpleTexturedMat->PreEditChange(nullptr);
            SimpleTexturedMat->PostEditChange();
        }
    }

    if (!SimpleTexturedMat)
    {
        Json->SetStringField("error", "Failed to create parent material");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    int32 SetCount = 0;
    TArray<TSharedPtr<FJsonValue>> SetList;
    TArray<USkeletalMeshComponent*> SkeletalMeshes;
    TargetActor->GetComponents<USkeletalMeshComponent>(SkeletalMeshes);
    for (USkeletalMeshComponent* MeshComp : SkeletalMeshes)
    {
        int32 NumMaterials = MeshComp->GetNumMaterials();
        for (int32 i = 0; i < NumMaterials; i++)
        {
            UMaterialInterface* CurrentMat = MeshComp->GetMaterial(i);
            FString MatName = CurrentMat ? CurrentMat->GetName() : TEXT("");

            FString* TexPath = TextureMap.Find(MatName);
            if (TexPath)
            {
                UTexture2D* Tex = LoadObject<UTexture2D>(nullptr, **TexPath);
                if (Tex)
                {
                    UMaterialInstanceDynamic* DynMat = UMaterialInstanceDynamic::Create(SimpleTexturedMat, MeshComp);
                    if (DynMat)
                    {
                        // Try common texture parameter names
                        DynMat->SetTextureParameterValue(TEXT("BaseColorTexture"), Tex);
                        DynMat->SetTextureParameterValue(TEXT("BaseColor"), Tex);
                        DynMat->SetTextureParameterValue(TEXT("Diffuse"), Tex);
                        DynMat->SetTextureParameterValue(TEXT("Texture"), Tex);
                        DynMat->SetTextureParameterValue(TEXT("TextureSample"), Tex);
                        MeshComp->SetMaterial(i, DynMat);

                        TSharedPtr<FJsonObject> Info = MakeShared<FJsonObject>();
                        Info->SetNumberField("slot_index", i);
                        Info->SetStringField("material_name", MatName);
                        Info->SetStringField("texture", Tex->GetName());
                        SetList.Add(MakeShared<FJsonValueObject>(Info));
                        SetCount++;
                        LUTE_LOG("Set texture %s for material %s on %s", *Tex->GetName(), *MatName, *TargetActor->GetName());
                    }
                }
            }
        }
    }

    Json->SetBoolField("success", SetCount > 0);
    Json->SetNumberField("set_count", SetCount);
    Json->SetArrayField("materials_set", SetList);
    Json->SetStringField("actor", TargetActor->GetName());

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /setup_materials — create textured materials for any character using a provided texture map
static bool HandleSetupMaterials(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString ActorName = Body->GetStringField(TEXT("actor_name"));
    if (ActorName.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'actor_name'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Parse texture_map from request body
    TMap<FString, FString> TextureMap;
    const TSharedPtr<FJsonObject>* TexMapObj = nullptr;
    if (Body->TryGetObjectField(TEXT("texture_map"), TexMapObj) && TexMapObj->IsValid())
    {
        for (const auto& Pair : (*TexMapObj)->Values)
        {
            TextureMap.Add(Pair.Key, Pair.Value->AsString());
        }
    }

    if (TextureMap.Num() == 0)
    {
        Json->SetStringField("error", "Missing or empty 'texture_map' object");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    AActor* TargetActor = nullptr;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetName() == ActorName)
        {
            TargetActor = *It;
            break;
        }
    }

    if (!TargetActor)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Actor not found: %s"), *ActorName));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Create a simple parent material with a texture parameter
    static UMaterial* GenericTexturedMat = nullptr;
    if (!GenericTexturedMat)
    {
        GenericTexturedMat = NewObject<UMaterial>(GetTransientPackage(), TEXT("GenericTexturedMat"), RF_Transient);
        if (GenericTexturedMat)
        {
            UMaterialExpressionTextureSampleParameter2D* TexSample = NewObject<UMaterialExpressionTextureSampleParameter2D>(GenericTexturedMat);
            TexSample->ParameterName = TEXT("BaseColorTexture");
            TexSample->SamplerType = SAMPLERTYPE_Color;
            GenericTexturedMat->GetExpressionCollection().AddExpression(TexSample);

            UMaterialEditorOnlyData* EditorData = GenericTexturedMat->GetEditorOnlyData();
            if (EditorData)
            {
                EditorData->BaseColor.Expression = TexSample;
                EditorData->BaseColor.OutputIndex = 0;
            }

            GenericTexturedMat->PreEditChange(nullptr);
            GenericTexturedMat->PostEditChange();
        }
    }

    if (!GenericTexturedMat)
    {
        Json->SetStringField("error", "Failed to create parent material");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    int32 SetCount = 0;
    TArray<TSharedPtr<FJsonValue>> SetList;
    TArray<USkeletalMeshComponent*> SkeletalMeshes;
    TargetActor->GetComponents<USkeletalMeshComponent>(SkeletalMeshes);
    for (USkeletalMeshComponent* MeshComp : SkeletalMeshes)
    {
        int32 NumMaterials = MeshComp->GetNumMaterials();
        for (int32 i = 0; i < NumMaterials; i++)
        {
            UMaterialInterface* CurrentMat = MeshComp->GetMaterial(i);
            FString MatName = CurrentMat ? CurrentMat->GetName() : TEXT("");

            FString* TexPath = TextureMap.Find(MatName);
            if (TexPath)
            {
                UTexture2D* Tex = LoadObject<UTexture2D>(nullptr, **TexPath);
                if (Tex)
                {
                    UMaterialInstanceDynamic* DynMat = UMaterialInstanceDynamic::Create(GenericTexturedMat, MeshComp);
                    if (DynMat)
                    {
                        DynMat->SetTextureParameterValue(TEXT("BaseColorTexture"), Tex);
                        MeshComp->SetMaterial(i, DynMat);

                        TSharedPtr<FJsonObject> Info = MakeShared<FJsonObject>();
                        Info->SetNumberField("slot_index", i);
                        Info->SetStringField("material_name", MatName);
                        Info->SetStringField("texture", Tex->GetName());
                        SetList.Add(MakeShared<FJsonValueObject>(Info));
                        SetCount++;
                        LUTE_LOG("Set texture %s for material %s on %s", *Tex->GetName(), *MatName, *TargetActor->GetName());
                    }
                }
            }
            else
            {
                // Try matching by slot index if texture_map uses numeric keys
                FString SlotKey = FString::FromInt(i);
                FString* TexPathBySlot = TextureMap.Find(SlotKey);
                if (TexPathBySlot)
                {
                    UTexture2D* Tex = LoadObject<UTexture2D>(nullptr, **TexPathBySlot);
                    if (Tex)
                    {
                        UMaterialInstanceDynamic* DynMat = UMaterialInstanceDynamic::Create(GenericTexturedMat, MeshComp);
                        if (DynMat)
                        {
                            DynMat->SetTextureParameterValue(TEXT("BaseColorTexture"), Tex);
                            MeshComp->SetMaterial(i, DynMat);

                            TSharedPtr<FJsonObject> Info = MakeShared<FJsonObject>();
                            Info->SetNumberField("slot_index", i);
                            Info->SetStringField("material_name", MatName);
                            Info->SetStringField("texture", Tex->GetName());
                            SetList.Add(MakeShared<FJsonValueObject>(Info));
                            SetCount++;
                            LUTE_LOG("Set texture %s for slot %d on %s", *Tex->GetName(), i, *TargetActor->GetName());
                        }
                    }
                }
            }
        }
    }

    Json->SetBoolField("success", SetCount > 0);
    Json->SetNumberField("set_count", SetCount);
    Json->SetArrayField("materials_set", SetList);
    Json->SetStringField("actor", TargetActor->GetName());

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /load_asset — debug: try to load any asset and report its type
static bool HandleLoadAsset(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString AssetPath = Body->GetStringField(TEXT("asset_path"));
    if (AssetPath.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'asset_path'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Try to load as UObject
    UObject* LoadedObj = LoadObject<UObject>(nullptr, *AssetPath);
    if (LoadedObj)
    {
        Json->SetBoolField("loaded", true);
        Json->SetStringField("class", LoadedObj->GetClass()->GetName());
        Json->SetStringField("name", LoadedObj->GetName());
        Json->SetStringField("path", LoadedObj->GetPathName());
        LUTE_LOG("Loaded asset %s as %s", *AssetPath, *LoadedObj->GetClass()->GetName());
    }
    else
    {
        // Try with .ObjectName suffix
        FString FullPath = AssetPath + TEXT(".") + FPaths::GetBaseFilename(AssetPath);
        LoadedObj = LoadObject<UObject>(nullptr, *FullPath);
        if (LoadedObj)
        {
            Json->SetBoolField("loaded", true);
            Json->SetStringField("class", LoadedObj->GetClass()->GetName());
            Json->SetStringField("name", LoadedObj->GetName());
            Json->SetStringField("path", LoadedObj->GetPathName());
            Json->SetStringField("tried_path", FullPath);
            LUTE_LOG("Loaded asset %s as %s", *FullPath, *LoadedObj->GetClass()->GetName());
        }
        else
        {
            Json->SetBoolField("loaded", false);
            Json->SetStringField("error", FString::Printf(TEXT("Failed to load: %s"), *AssetPath));

            // Also try searching asset registry
            FAssetRegistryModule& AssetRegistryModule = FModuleManager::LoadModuleChecked<FAssetRegistryModule>("AssetRegistry");
            IAssetRegistry& AssetRegistry = AssetRegistryModule.Get();
            TArray<FAssetData> AssetData;
            AssetRegistry.GetAssetsByPath(FName(*FPaths::GetPath(AssetPath)), AssetData);
            TArray<TSharedPtr<FJsonValue>> FoundAssets;
            for (const FAssetData& Data : AssetData)
            {
                TSharedPtr<FJsonObject> AssetInfo = MakeShared<FJsonObject>();
                AssetInfo->SetStringField("name", Data.AssetName.ToString());
                AssetInfo->SetStringField("class", Data.AssetClass.ToString());
                AssetInfo->SetStringField("package", Data.PackageName.ToString());
                AssetInfo->SetStringField("object_path", Data.GetObjectPathString());
                FoundAssets.Add(MakeShared<FJsonValueObject>(AssetInfo));
            }
            Json->SetArrayField("assets_in_path", FoundAssets);
        }
    }

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /add_mesh — add a skeletal mesh component to an actor
static bool HandleAddMesh(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString ActorName = Body->GetStringField(TEXT("actor_name"));
    FString MeshPath = Body->GetStringField(TEXT("mesh_path"));
    bool bVisible = Body->GetBoolField(TEXT("visible"));

    if (ActorName.IsEmpty() || MeshPath.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'actor_name' or 'mesh_path'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    AActor* TargetActor = nullptr;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetName() == ActorName)
        {
            TargetActor = *It;
            break;
        }
    }

    if (!TargetActor)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Actor not found: %s"), *ActorName));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    USkeletalMesh* SkeletalMesh = LoadObject<USkeletalMesh>(nullptr, *MeshPath);
    if (!SkeletalMesh)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Failed to load mesh: %s"), *MeshPath));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Find the master mesh component to use as leader for pose
    USkeletalMeshComponent* MasterComp = TargetActor->FindComponentByClass<USkeletalMeshComponent>();

    // Create new skeletal mesh component
    USkeletalMeshComponent* NewMeshComp = NewObject<USkeletalMeshComponent>(TargetActor, FName(*MeshPath));
    NewMeshComp->SetSkeletalMesh(SkeletalMesh);
    NewMeshComp->SetupAttachment(MasterComp ? MasterComp : TargetActor->GetRootComponent());
    NewMeshComp->RegisterComponent();
    NewMeshComp->SetVisibility(bVisible);

    // If we have a master, set this as follower
    if (MasterComp)
    {
        NewMeshComp->SetLeaderPoseComponent(MasterComp);
    }

    Json->SetBoolField("success", true);
    Json->SetStringField("actor", TargetActor->GetName());
    Json->SetStringField("component_name", NewMeshComp->GetName());
    Json->SetBoolField("visible", bVisible);
    LUTE_LOG("Added mesh %s to %s (visible=%s)", *MeshPath, *TargetActor->GetName(), bVisible ? TEXT("true") : TEXT("false"));

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /toggle_mesh — toggle visibility of a named mesh component on an actor
static bool HandleToggleMesh(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString ActorName = Body->GetStringField(TEXT("actor_name"));
    FString MeshName = Body->GetStringField(TEXT("mesh_name"));
    bool bVisible = Body->GetBoolField(TEXT("visible"));

    if (ActorName.IsEmpty() || MeshName.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'actor_name' or 'mesh_name'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    AActor* TargetActor = nullptr;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetName() == ActorName)
        {
            TargetActor = *It;
            break;
        }
    }

    if (!TargetActor)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Actor not found: %s"), *ActorName));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Find all skeletal mesh components and toggle ones matching the name
    TArray<USkeletalMeshComponent*> MeshComponents;
    TargetActor->GetComponents<USkeletalMeshComponent>(MeshComponents);

    int32 ToggledCount = 0;
    for (USkeletalMeshComponent* MeshComp : MeshComponents)
    {
        FString CompName = MeshComp->GetName();
        if (CompName.Contains(MeshName))
        {
            MeshComp->SetVisibility(bVisible);
            ToggledCount++;
            LUTE_LOG("Toggled %s on %s -> %s", *CompName, *ActorName, bVisible ? TEXT("visible") : TEXT("hidden"));
        }
    }

    Json->SetBoolField("success", ToggledCount > 0);
    Json->SetNumberField("toggled_count", ToggledCount);
    Json->SetStringField("mesh_name", MeshName);
    Json->SetBoolField("visible", bVisible);

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Get or create a shared WorldBuilder actor for attaching prop components
static AActor* GetOrCreateWorldBuilderActor(UWorld* World)
{
    // Search for existing WorldBuilder actor by label
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetActorLabel().StartsWith(TEXT("WorldBuilder_")))
        {
            return *It;
        }
    }
    // Create a new one — let UE auto-generate a unique name to avoid "cannot generate unique name" crash
    FActorSpawnParameters SpawnParams;
    AActor* BuilderActor = World->SpawnActor<AActor>(AActor::StaticClass(), FVector::ZeroVector, FRotator::ZeroRotator, SpawnParams);
    if (BuilderActor)
    {
        BuilderActor->SetActorLabel(TEXT("WorldBuilder_Props"));
        // Ensure it has a root scene component so child components inherit correct transforms
        if (!BuilderActor->GetRootComponent())
        {
            USceneComponent* Root = NewObject<USceneComponent>(BuilderActor, TEXT("Root"));
            Root->RegisterComponent();
            Root->SetMobility(EComponentMobility::Static);
            BuilderActor->SetRootComponent(Root);
            Root->SetWorldLocation(FVector::ZeroVector);
        }
    }
    return BuilderActor;
}

// Handle: POST /place_prop — place a static mesh prop at a location
static bool HandlePlaceProp(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString MeshPath = Body->GetStringField(TEXT("mesh_path"));
    double X = Body->GetNumberField(TEXT("x"));
    double Y = Body->GetNumberField(TEXT("y"));
    double Z = Body->GetNumberField(TEXT("z"));
    double Pitch = Body->GetNumberField(TEXT("pitch"));
    double Yaw = Body->GetNumberField(TEXT("yaw"));
    double Roll = Body->GetNumberField(TEXT("roll"));
    double Scale = Body->GetNumberField(TEXT("scale"));
    if (Scale == 0.0) Scale = 1.0;

    if (MeshPath.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'mesh_path'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    UStaticMesh* Mesh = LoadObject<UStaticMesh>(nullptr, *MeshPath);
    if (!Mesh)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Failed to load mesh: %s"), *MeshPath));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Use shared parent actor — attach as component instead of spawning new actor
    AActor* BuilderActor = GetOrCreateWorldBuilderActor(World);
    if (!BuilderActor)
    {
        Json->SetStringField("error", "Failed to get/create WorldBuilder actor");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Create a new static mesh component and attach to the builder actor
    FString CompName = FString::Printf(TEXT("Prop_%s_%d"), *Mesh->GetName(), FMath::Rand());
    UStaticMeshComponent* MeshComp = NewObject<UStaticMeshComponent>(BuilderActor, FName(*CompName));
    if (MeshComp)
    {
        MeshComp->SetStaticMesh(Mesh);
        MeshComp->SetMobility(EComponentMobility::Movable);
        MeshComp->SetupAttachment(BuilderActor->GetRootComponent());
        MeshComp->RegisterComponent();

        // Set world transform AFTER registration — otherwise it gets reset
        MeshComp->SetWorldLocation(FVector(X, Y, Z));
        MeshComp->SetWorldRotation(FRotator(Pitch, Yaw, Roll));
        MeshComp->SetWorldScale3D(FVector(Scale));

        Json->SetBoolField("success", true);
        Json->SetStringField("actor_name", BuilderActor->GetName());
        Json->SetStringField("component", CompName);
        Json->SetStringField("mesh", Mesh->GetName());
        LUTE_LOG("Placed prop %s at (%.1f, %.1f, %.1f) as component", *Mesh->GetName(), X, Y, Z);
    }
    else
    {
        Json->SetStringField("error", "Failed to create mesh component");
    }

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /list_props — list available static mesh assets
static bool HandleListProps(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    FString SearchFilter = Body.IsValid() ? Body->GetStringField(TEXT("filter")) : TEXT("");
    int32 MaxResults = Body.IsValid() ? (int32)Body->GetNumberField(TEXT("max")) : 200;
    if (MaxResults <= 0) MaxResults = 200;

    FAssetRegistryModule& AssetRegistryModule = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry"));
    IAssetRegistry& AssetRegistry = AssetRegistryModule.Get();

    TArray<FAssetData> AssetData;
    AssetRegistry.GetAssetsByClass(FTopLevelAssetPath(TEXT("/Script/Engine.StaticMesh")), AssetData);

    TArray<TSharedPtr<FJsonValue>> PropList;
    int32 Count = 0;
    for (const FAssetData& Data : AssetData)
    {
        FString AssetName = Data.AssetName.ToString();
        FString AssetPath = Data.GetObjectPathString();

        if (!SearchFilter.IsEmpty() && !AssetName.Contains(SearchFilter))
            continue;

        TSharedPtr<FJsonObject> PropInfo = MakeShared<FJsonObject>();
        PropInfo->SetStringField("name", AssetName);
        PropInfo->SetStringField("path", AssetPath);
        PropList.Add(MakeShared<FJsonValueObject>(PropInfo));
        Count++;
        if (Count >= MaxResults) break;
    }

    Json->SetBoolField("success", true);
    Json->SetNumberField("count", Count);
    Json->SetArrayField("props", PropList);

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /world_build — batch place multiple props from a JSON array
static bool HandleWorldBuild(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    const TArray<TSharedPtr<FJsonValue>>* Placements = nullptr;
    if (!Body->TryGetArrayField(TEXT("placements"), Placements))
    {
        Json->SetStringField("error", "Missing 'placements' array");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Use shared parent actor
    AActor* BuilderActor = GetOrCreateWorldBuilderActor(World);
    if (!BuilderActor)
    {
        Json->SetStringField("error", "Failed to get/create WorldBuilder actor");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    int32 PlacedCount = 0;
    int32 FailedCount = 0;
    TArray<TSharedPtr<FJsonValue>> Results;

    for (const TSharedPtr<FJsonValue>& PlacementVal : *Placements)
    {
        TSharedPtr<FJsonObject> P = PlacementVal->AsObject();
        if (!P.IsValid()) continue;

        FString MeshPath = P->GetStringField(TEXT("mesh_path"));
        double X = P->GetNumberField(TEXT("x"));
        double Y = P->GetNumberField(TEXT("y"));
        double Z = P->GetNumberField(TEXT("z"));
        double Yaw = P->GetNumberField(TEXT("yaw"));
        double Scale = P->GetNumberField(TEXT("scale"));
        if (Scale == 0.0) Scale = 1.0;

        if (MeshPath.IsEmpty()) { FailedCount++; continue; }

        UStaticMesh* Mesh = LoadObject<UStaticMesh>(nullptr, *MeshPath);
        if (!Mesh) { FailedCount++; continue; }

        FString CompName = FString::Printf(TEXT("WP_%s_%d"), *Mesh->GetName(), FMath::Rand());
        UStaticMeshComponent* MeshComp = NewObject<UStaticMeshComponent>(BuilderActor, FName(*CompName));
        if (MeshComp)
        {
            MeshComp->SetStaticMesh(Mesh);
            MeshComp->SetMobility(EComponentMobility::Movable);
            MeshComp->SetupAttachment(BuilderActor->GetRootComponent());
            MeshComp->RegisterComponent();

            // Set world transform AFTER registration
            MeshComp->SetWorldLocation(FVector(X, Y, Z));
            MeshComp->SetWorldRotation(FRotator(0, Yaw, 0));
            MeshComp->SetWorldScale3D(FVector(Scale));

            TSharedPtr<FJsonObject> R = MakeShared<FJsonObject>();
            R->SetBoolField("success", true);
            R->SetStringField("mesh", Mesh->GetName());
            R->SetStringField("component", CompName);
            Results.Add(MakeShared<FJsonValueObject>(R));
            PlacedCount++;
        }
        else
        {
            FailedCount++;
        }
    }

    Json->SetBoolField("success", PlacedCount > 0);
    Json->SetNumberField("placed_count", PlacedCount);
    Json->SetNumberField("failed_count", FailedCount);
    Json->SetArrayField("results", Results);

    LUTE_LOG("World build: placed %d, failed %d", PlacedCount, FailedCount);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /batch_place — batch place identical meshes using HISM (single draw call)
static bool HandleBatchPlace(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString MeshPath = Body->GetStringField(TEXT("mesh_path"));
    if (MeshPath.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'mesh_path'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    const TArray<TSharedPtr<FJsonValue>>* Placements = nullptr;
    if (!Body->TryGetArrayField(TEXT("placements"), Placements))
    {
        Json->SetStringField("error", "Missing 'placements' array");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    UStaticMesh* Mesh = LoadObject<UStaticMesh>(nullptr, *MeshPath);
    if (!Mesh)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Failed to load mesh: %s"), *MeshPath));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Get or create WorldBuilder parent actor
    AActor* BuilderActor = GetOrCreateWorldBuilderActor(World);
    if (!BuilderActor)
    {
        Json->SetStringField("error", "Failed to get/create WorldBuilder actor");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Find existing HISM for this mesh, or create a new one
    UHierarchicalInstancedStaticMeshComponent* HISM = nullptr;
    FString HISMName = FString::Printf(TEXT("HISM_%s"), *Mesh->GetName());

    // Search for existing HISM with this mesh on the builder actor
    TArray<UActorComponent*> ExistingComps;
    BuilderActor->GetComponents(UHierarchicalInstancedStaticMeshComponent::StaticClass(), ExistingComps);
    for (UActorComponent* Comp : ExistingComps)
    {
        UHierarchicalInstancedStaticMeshComponent* ExistingHISM = Cast<UHierarchicalInstancedStaticMeshComponent>(Comp);
        if (ExistingHISM && ExistingHISM->GetStaticMesh() == Mesh)
        {
            HISM = ExistingHISM;
            break;
        }
    }

    if (!HISM)
    {
        HISM = NewObject<UHierarchicalInstancedStaticMeshComponent>(BuilderActor, FName(*HISMName));
        if (!HISM)
        {
            Json->SetStringField("error", "Failed to create HISM component");
            Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
            return true;
        }
        HISM->SetStaticMesh(Mesh);
        HISM->SetMobility(EComponentMobility::Movable);
        HISM->SetupAttachment(BuilderActor->GetRootComponent());
        HISM->RegisterComponent();
        HISM->SetWorldLocation(FVector::ZeroVector);
        HISM->SetWorldRotation(FRotator::ZeroRotator);
        HISM->SetWorldScale3D(FVector::OneVector);
        HISM->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        HISM->SetCastShadow(true);
    }

    // Add all instances
    int32 InstanceCount = 0;
    int32 FailedCount = 0;

    for (const TSharedPtr<FJsonValue>& PlacementVal : *Placements)
    {
        TSharedPtr<FJsonObject> P = PlacementVal->AsObject();
        if (!P.IsValid()) { FailedCount++; continue; }

        double X = P->GetNumberField(TEXT("x"));
        double Y = P->GetNumberField(TEXT("y"));
        double Z = P->HasField(TEXT("z")) ? P->GetNumberField(TEXT("z")) : 0.0;
        double Yaw = P->HasField(TEXT("yaw")) ? P->GetNumberField(TEXT("yaw")) : 0.0;
        double Scale = P->HasField(TEXT("scale")) ? P->GetNumberField(TEXT("scale")) : 1.0;

        FTransform InstanceTransform(FRotator(0, Yaw, 0), FVector(X, Y, Z), FVector(Scale));
        int32 InstanceIdx = HISM->AddInstance(InstanceTransform);
        InstanceCount++;
    }

    Json->SetBoolField("success", InstanceCount > 0);
    Json->SetNumberField("instance_count", InstanceCount);
    Json->SetNumberField("failed_count", FailedCount);
    Json->SetStringField("mesh", Mesh->GetName());
    Json->SetStringField("hism_component", HISMName);
    LUTE_LOG("BatchPlace: %d instances of %s via HISM", InstanceCount, *Mesh->GetName());
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /exec — execute a game action
static bool HandleExec(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString Action = Body->GetStringField(TEXT("action"));
    LUTE_LOG("Exec: %s", *Action);

    Json->SetStringField("action", Action);
    Json->SetBoolField("success", true);

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /create_landscape — delete existing landscape and create a new one with specified size
static bool HandleCreateLandscape(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    int32 GridSize = Body->HasField(TEXT("grid_size")) ? (int32)Body->GetNumberField(TEXT("grid_size")) : 681;
    double ScalePerQuad = Body->HasField(TEXT("scale")) ? Body->GetNumberField(TEXT("scale")) : 100.0;

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Delete existing landscape(s) and scatter actors
    int32 DeletedCount = 0;
    TArray<AActor*> ToRemove;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        AActor* Actor = *It;
        if (!Actor) continue;
        if (Actor->IsA<ALandscape>() || Actor->IsA<ALandscapeProxy>())
        {
            ToRemove.Add(Actor);
            continue;
        }
        FString Name = Actor->GetName();
        FString Label = Actor->GetActorLabel();
        if (Name.StartsWith(TEXT("WorldBuilder_")) || Label.StartsWith(TEXT("WorldBuilder_")) ||
            Name.StartsWith(TEXT("ScatterCell_")) || Label.StartsWith(TEXT("ScatterCell_")))
        {
            ToRemove.Add(Actor);
        }
    }
    for (AActor* Actor : ToRemove)
    {
        if (Actor && IsValid(Actor))
        {
            World->DestroyActor(Actor, false, false);
            DeletedCount++;
        }
    }

    LUTE_LOG("CreateLandscape: Deleted %d actors", DeletedCount);

    // Calculate component layout
    int32 SectionSize = 255;
    int32 NumSectionsPerAxis = FMath::CeilToInt((float)GridSize / (float)SectionSize);
    int32 SnappedGridSize = NumSectionsPerAxis * SectionSize;
    int32 NumComponents = NumSectionsPerAxis * NumSectionsPerAxis;

    // Spawn the landscape actor
    FActorSpawnParameters SpawnParams;
    SpawnParams.Name = FName(TEXT("Landscape"));
    SpawnParams.NameMode = FActorSpawnParameters::ESpawnActorNameMode::Requested;

    ALandscape* NewLandscape = World->SpawnActor<ALandscape>(FVector(0, 0, 0), FRotator::ZeroRotator, SpawnParams);
    if (!NewLandscape)
    {
        Json->SetStringField("error", "Failed to spawn landscape actor");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    NewLandscape->SetActorLabel(TEXT("Landscape"));

    // Set transform: position at origin, scale by quads
    FTransform LandscapeTransform(FRotator::ZeroRotator, FVector(0, 0, 0),
        FVector(ScalePerQuad, ScalePerQuad, 100.0f));
    NewLandscape->SetActorTransform(LandscapeTransform);

    // Create landscape components
    NewLandscape->LandscapeComponents.Reserve(NumComponents);
    for (int32 CompY = 0; CompY < NumSectionsPerAxis; CompY++)
    {
        for (int32 CompX = 0; CompX < NumSectionsPerAxis; CompX++)
        {
            int32 SectionBaseX = CompX * SectionSize;
            int32 SectionBaseY = CompY * SectionSize;
            FString CompName = FString::Printf(TEXT("LandscapeComponent_%d_%d"), SectionBaseX, SectionBaseY);

            ULandscapeComponent* Comp = NewObject<ULandscapeComponent>(NewLandscape, FName(*CompName), RF_Transactional);
            if (Comp)
            {
                Comp->SectionBaseX = SectionBaseX;
                Comp->SectionBaseY = SectionBaseY;
                Comp->ComponentSizeQuads = SectionSize;
                Comp->NumSubsections = 1;
                Comp->SubsectionSizeQuads = SectionSize;
                Comp->AttachToComponent(NewLandscape->GetRootComponent(), FAttachmentTransformRules::KeepRelativeTransform);
                Comp->RegisterComponent();
                NewLandscape->LandscapeComponents.Add(Comp);
            }
        }
    }

    // Create heightmap textures for each component (flat at sea level)
    for (ULandscapeComponent* Comp : NewLandscape->LandscapeComponents)
    {
        if (!Comp) continue;

        int32 TexSize = Comp->ComponentSizeQuads + 1;
        FString TexName = FString::Printf(TEXT("Heightmap_%d_%d"), Comp->SectionBaseX, Comp->SectionBaseY);

        UTexture2D* HeightmapTex = NewObject<UTexture2D>(Comp, FName(*TexName), RF_Transactional);
        if (HeightmapTex)
        {
            HeightmapTex->Source.Init(TexSize, TexSize, 1, 1, TSF_BGRA8);
            HeightmapTex->CompressionSettings = TC_HDR;
            HeightmapTex->SRGB = false;
            HeightmapTex->MipGenSettings = TMGS_NoMipmaps;
            HeightmapTex->UpdateResource();

            FTexture2DMipMap& Mip = HeightmapTex->GetPlatformData()->Mips[0];
            void* MipData = Mip.BulkData.Lock(LOCK_READ_WRITE);
            FColor* TexColors = reinterpret_cast<FColor*>(MipData);
            for (int32 i = 0; i < TexSize * TexSize; i++)
            {
                TexColors[i] = FColor(0, 128, 0, 255); // 32768 = sea level
            }
            Mip.BulkData.Unlock();
            HeightmapTex->UpdateResource();

            Comp->SetHeightmap(HeightmapTex);
        }
    }

    NewLandscape->RegisterAllComponents();
    NewLandscape->CreateLayer(TEXT("AutoLayer"));

    LUTE_LOG("CreateLandscape: Created %dx%d landscape (%d components, grid=%d, scale=%.0f, world=%.0fu)",
        NumSectionsPerAxis, NumSectionsPerAxis, NumComponents, SnappedGridSize, ScalePerQuad, SnappedGridSize * ScalePerQuad);

    Json->SetBoolField("success", true);
    Json->SetNumberField("grid_size", SnappedGridSize);
    Json->SetNumberField("sections_per_axis", NumSectionsPerAxis);
    Json->SetNumberField("component_size", SectionSize);
    Json->SetNumberField("num_components", NumComponents);
    Json->SetNumberField("scale_per_quad", ScalePerQuad);
    Json->SetNumberField("world_size", SnappedGridSize * ScalePerQuad);
    Json->SetNumberField("deleted_actors", DeletedCount);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /spawn_water — create a water plane at a given position and height
static bool HandleSpawnWater(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    double X = Body->GetNumberField(TEXT("x"));
    double Y = Body->GetNumberField(TEXT("y"));
    double Z = Body->GetNumberField(TEXT("z"));
    double SizeX = Body->GetNumberField(TEXT("size_x"));
    double SizeY = Body->GetNumberField(TEXT("size_y"));
    FString WaterMaterialPath = Body->GetStringField(TEXT("material"));

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Use a basic plane mesh scaled to the water size
    UStaticMesh* PlaneMesh = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Plane.Plane"));
    if (!PlaneMesh)
    {
        Json->SetStringField("error", "Failed to load plane mesh");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Spawn a standalone water plane actor (not on WorldBuilder to avoid component count issues)
    FActorSpawnParameters SpawnParams;
    SpawnParams.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    AActor* WaterActor = World->SpawnActor<AActor>(FVector(X, Y, Z), FRotator::ZeroRotator, SpawnParams);
    if (!WaterActor)
    {
        Json->SetStringField("error", "Failed to spawn water actor");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }
    WaterActor->SetActorLabel(TEXT("WaterPlane"));

    // Create a static mesh component on the water actor
    UStaticMeshComponent* WaterComp = NewObject<UStaticMeshComponent>(WaterActor, TEXT("WaterMesh"));
    if (!WaterComp)
    {
        Json->SetStringField("error", "Failed to create water component");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    WaterComp->SetStaticMesh(PlaneMesh);
    WaterComp->SetupAttachment(WaterActor->GetRootComponent());
    WaterComp->SetMobility(EComponentMobility::Movable);
    WaterComp->SetCastShadow(false);
    WaterComp->SetCollisionEnabled(ECollisionEnabled::NoCollision);
    WaterComp->SetCanEverAffectNavigation(false);
    // Plane mesh is 100x100 units by default, scale to desired size
    WaterComp->SetRelativeScale3D(FVector(SizeX / 100.0, SizeY / 100.0, 1.0));
    WaterComp->SetRelativeLocation(FVector(0, 0, 0));
    WaterComp->SetRelativeRotation(FRotator(0, 0, 0));
    WaterComp->RegisterComponent();

    // Try to load water material if specified
    if (!WaterMaterialPath.IsEmpty())
    {
        UMaterialInterface* WaterMat = LoadObject<UMaterialInterface>(nullptr, *WaterMaterialPath);
        if (WaterMat)
        {
            WaterComp->SetMaterial(0, WaterMat);
            LUTE_LOG("Water: applied material %s", *WaterMaterialPath);
        }
        else
        {
            LUTE_LOG("Water: material not found %s, using default", *WaterMaterialPath);
        }
    }

    Json->SetBoolField("success", true);
    Json->SetStringField("actor", WaterActor->GetActorLabel());
    Json->SetNumberField("x", X);
    Json->SetNumberField("y", Y);
    Json->SetNumberField("z", Z);
    Json->SetNumberField("size_x", SizeX);
    Json->SetNumberField("size_y", SizeY);
    LUTE_LOG("Water: spawned plane at (%.0f, %.0f, %.0f) size %.0fx%.0f", X, Y, Z, SizeX, SizeY);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /terrain/scatter — spawn scatter instances partitioned into WP grid cells
// Each cell gets its own actor with HISM components, so WP streams them in/out per cell.
static bool HandleTerrainScatter(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString MeshPath = Body->GetStringField(TEXT("mesh_path"));
    if (MeshPath.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'mesh_path'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    const TArray<TSharedPtr<FJsonValue>>* Placements = nullptr;
    if (!Body->TryGetArrayField(TEXT("placements"), Placements))
    {
        Json->SetStringField("error", "Missing 'placements' array");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Cell size in world units (default 25600 = 256m, matching WP default)
    double CellSize = Body->HasField(TEXT("cell_size")) ? Body->GetNumberField(TEXT("cell_size")) : 25600.0;

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    UStaticMesh* Mesh = LoadObject<UStaticMesh>(nullptr, *MeshPath);
    if (!Mesh)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Failed to load mesh: %s"), *MeshPath));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Group placements by grid cell
    TMap<FIntPoint, TArray<TSharedPtr<FJsonObject>>> CellBuckets;
    int32 TotalCount = 0;
    int32 FailedCount = 0;

    for (const TSharedPtr<FJsonValue>& PlacementVal : *Placements)
    {
        TSharedPtr<FJsonObject> P = PlacementVal->AsObject();
        if (!P.IsValid()) { FailedCount++; continue; }

        double X = P->GetNumberField(TEXT("x"));
        double Y = P->GetNumberField(TEXT("y"));

        // Compute cell coordinates
        int32 CellX = FMath::FloorToInt(X / CellSize);
        int32 CellY = FMath::FloorToInt(Y / CellSize);
        FIntPoint CellKey(CellX, CellY);

        CellBuckets.FindOrAdd(CellKey).Add(P);
        TotalCount++;
    }

    FString MeshName = Mesh->GetName();
    int32 CellActorsCreated = 0;
    int32 InstanceCount = 0;

    // Use a single WorldBuilder actor like /batch_place does, with one HISM per cell+mesh
    AActor* BuilderActor = GetOrCreateWorldBuilderActor(World);
    if (!BuilderActor)
    {
        Json->SetStringField("error", "Failed to get/create WorldBuilder actor");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Safety: if too many cells, skip cell partitioning to avoid creating too many HISM components
    bool bPartitionByCell = CellBuckets.Num() <= 64;
    if (!bPartitionByCell)
    {
        LUTE_LOG("Scatter: %d cells exceeds limit 64, skipping cell partitioning", CellBuckets.Num());
    }

    for (const auto& CellPair : CellBuckets)
    {
        FIntPoint CellKey = CellPair.Key;
        const TArray<TSharedPtr<FJsonObject>>& CellPlacements = CellPair.Value;

        // HISM component name encodes both cell and mesh (or just mesh if not partitioning)
        FString HISMName = bPartitionByCell
            ? FString::Printf(TEXT("ScatterCell_%d_%d_%s"), CellKey.X, CellKey.Y, *MeshName)
            : FString::Printf(TEXT("HISM_%s"), *MeshName);

        // Find existing HISM for this cell+mesh on the builder actor
        UHierarchicalInstancedStaticMeshComponent* HISM = nullptr;
        TArray<UActorComponent*> ExistingComps;
        BuilderActor->GetComponents(UHierarchicalInstancedStaticMeshComponent::StaticClass(), ExistingComps);
        for (UActorComponent* Comp : ExistingComps)
        {
            UHierarchicalInstancedStaticMeshComponent* ExistingHISM = Cast<UHierarchicalInstancedStaticMeshComponent>(Comp);
            if (ExistingHISM && ExistingHISM->GetStaticMesh() == Mesh && ExistingHISM->GetName() == HISMName)
            {
                HISM = ExistingHISM;
                break;
            }
        }

        if (!HISM)
        {
            HISM = NewObject<UHierarchicalInstancedStaticMeshComponent>(BuilderActor, FName(*HISMName));
            if (!HISM) continue;
            HISM->SetStaticMesh(Mesh);
            HISM->SetMobility(EComponentMobility::Movable);
            HISM->SetupAttachment(BuilderActor->GetRootComponent());
            HISM->RegisterComponent();
            HISM->SetWorldLocation(FVector::ZeroVector);
            HISM->SetWorldRotation(FRotator::ZeroRotator);
            HISM->SetWorldScale3D(FVector::OneVector);
            HISM->SetCollisionEnabled(ECollisionEnabled::NoCollision);
            HISM->SetCastShadow(true);
        }

        CellActorsCreated++;

        // Add instances to this cell's HISM
        for (const TSharedPtr<FJsonObject>& P : CellPlacements)
        {
            double X = P->GetNumberField(TEXT("x"));
            double Y = P->GetNumberField(TEXT("y"));
            double Z = P->HasField(TEXT("z")) ? P->GetNumberField(TEXT("z")) : 0.0;
            double Yaw = P->HasField(TEXT("yaw")) ? P->GetNumberField(TEXT("yaw")) : 0.0;
            double Scale = P->HasField(TEXT("scale")) ? P->GetNumberField(TEXT("scale")) : 1.0;

            FTransform InstanceTransform(FRotator(0, Yaw, 0), FVector(X, Y, Z), FVector(Scale));
            HISM->AddInstance(InstanceTransform);
            InstanceCount++;
        }
    }

    Json->SetBoolField("success", InstanceCount > 0);
    Json->SetNumberField("instance_count", InstanceCount);
    Json->SetNumberField("cell_actors", CellActorsCreated);
    Json->SetNumberField("failed_count", FailedCount);
    Json->SetStringField("mesh", MeshName);
    Json->SetNumberField("cell_size", CellSize);
    LUTE_LOG("Scatter: %d instances of %s across %d cells (cell_size=%.0f)", InstanceCount, *MeshName, CellActorsCreated, CellSize);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /clear_world — remove all WorldBuilder actors and their components
static bool HandleClearWorld(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();
    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    int32 RemovedCount = 0;

    // First: find and destroy WorldBuilder parent actors (this removes all attached components)
    TArray<AActor*> ToRemove;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        AActor* Actor = *It;
        if (!Actor) continue;
        FString Name = Actor->GetName();
        FString Label = Actor->GetActorLabel();
        if (Name.StartsWith(TEXT("WorldBuilder_")) || Label.StartsWith(TEXT("WorldBuilder_")) ||
            Name.StartsWith(TEXT("ScatterCell_")) || Label.StartsWith(TEXT("ScatterCell_")) ||
            Name.StartsWith(TEXT("WaterPlane")) || Label.StartsWith(TEXT("WaterPlane")))
        {
            ToRemove.Add(Actor);
        }
    }
    for (AActor* Actor : ToRemove)
    {
        // Unregister all components first to reduce crash risk
        TArray<UActorComponent*> Comps;
        Actor->GetComponents(Comps);
        for (UActorComponent* Comp : Comps)
        {
            if (Comp && Comp->IsRegistered())
            {
                Comp->UnregisterComponent();
            }
        }
        // Use conditional destroy to avoid crashing if actor is already pending kill
        if (Actor && IsValid(Actor))
        {
            World->DestroyActor(Actor, false, false);
        }
        RemovedCount++;
    }

    // Also clean up any stray individual static mesh actors from old runs
    TArray<AActor*> StrayActors;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        AActor* Actor = *It;
        if (!Actor) continue;
        FString Name = Actor->GetName();
        if (Name.StartsWith(TEXT("Prop_")) || Name.StartsWith(TEXT("WP_")))
        {
            StrayActors.Add(Actor);
        }
    }
    for (AActor* Actor : StrayActors)
    {
        if (Actor && IsValid(Actor))
        {
            World->DestroyActor(Actor, false, false);
        }
        RemovedCount++;
    }

    // Defer garbage collection to next tick — forced synchronous GC during HTTP callback can crash
    // The engine will naturally collect on the next frame

    Json->SetBoolField("success", true);
    Json->SetNumberField("removed_count", RemovedCount);
    LUTE_LOG("ClearWorld: removed %d actors (GC deferred)", RemovedCount);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /delete_actor — delete a specific actor by name
static bool HandleDeleteActor(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString ActorName = Body->GetStringField(TEXT("actor_name"));
    if (ActorName.IsEmpty())
    {
        Json->SetStringField("error", "Missing 'actor_name'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    bool bFound = false;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetName() == ActorName || It->GetActorLabel() == ActorName)
        {
            World->DestroyActor(*It, false);
            bFound = true;
            break;
        }
    }

    Json->SetBoolField("success", bFound);
    if (!bFound) Json->SetStringField("error", "Actor not found");
    LUTE_LOG("DeleteActor: %s %s", *ActorName, bFound ? TEXT("success") : TEXT("not found"));
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /move_actor — move an existing actor to new position
static bool HandleMoveActor(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString ActorName = Body->GetStringField(TEXT("actor_name"));
    double X = Body->GetNumberField(TEXT("x"));
    double Y = Body->GetNumberField(TEXT("y"));
    double Z = Body->GetNumberField(TEXT("z"));
    double Yaw = Body->GetNumberField(TEXT("yaw"));

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    bool bFound = false;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetName() == ActorName || It->GetActorLabel() == ActorName)
        {
            It->SetActorLocation(FVector(X, Y, Z));
            if (Yaw != 0.0)
            {
                FRotator Rot = It->GetActorRotation();
                Rot.Yaw = Yaw;
                It->SetActorRotation(Rot);
            }
            bFound = true;
            break;
        }
    }

    Json->SetBoolField("success", bFound);
    if (!bFound) Json->SetStringField("error", "Actor not found");
    LUTE_LOG("MoveActor: %s to (%.1f, %.1f, %.1f)", *ActorName, X, Y, Z);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /duplicate_actor — duplicate an actor at a new position
static bool HandleDuplicateActor(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString ActorName = Body->GetStringField(TEXT("actor_name"));
    double X = Body->GetNumberField(TEXT("x"));
    double Y = Body->GetNumberField(TEXT("y"));
    double Z = Body->GetNumberField(TEXT("z"));

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    bool bFound = false;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        if (It->GetName() == ActorName || It->GetActorLabel() == ActorName)
        {
            AActor* Dup = Cast<AActor>(StaticDuplicateObject(*It, World));
            if (Dup)
            {
                Dup->Rename(nullptr, World);
                Dup->SetActorLocation(FVector(X, Y, Z));
                // Register the duplicated actor
                Dup->GetRootComponent()->RegisterComponent();
                bFound = true;
            }
            break;
        }
    }

    Json->SetBoolField("success", bFound);
    if (!bFound) Json->SetStringField("error", "Actor not found");
    LUTE_LOG("DuplicateActor: %s at (%.1f, %.1f, %.1f)", *ActorName, X, Y, Z);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /setup_landscape_material — create a landscape material with 4 layers and assign it
static bool HandleSetupLandscapeMaterial(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();
    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    ALandscapeProxy* Landscape = nullptr;
    for (TActorIterator<ALandscape> It(World); It; ++It)
    {
        Landscape = *It;
        break;
    }
    if (!Landscape)
    {
        Json->SetStringField("error", "No landscape found");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Create a landscape material asset
    FString AssetPath = TEXT("/Game/LandscapeMaterials/M_LandscapeTerrain");
    UMaterial* Mat = LoadObject<UMaterial>(nullptr, *AssetPath);

    if (!Mat)
    {
        UPackage* Package = CreatePackage(TEXT("/Game/LandscapeMaterials"));
        if (Package)
        {
            Mat = NewObject<UMaterial>(Package, TEXT("M_LandscapeTerrain"), RF_Public | RF_Standalone);
        }
    }

    if (!Mat)
    {
        Json->SetStringField("error", "Failed to create material");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Layer colors: Sand (tan), Grass (green), Dirt (brown), Rock (gray)
    struct LayerColor { const TCHAR* Name; FLinearColor Color; };
    LayerColor LayerColors[] = {
        { TEXT("Sand"),  FLinearColor(0.76f, 0.70f, 0.50f, 1.0f) },
        { TEXT("Grass"), FLinearColor(0.30f, 0.50f, 0.20f, 1.0f) },
        { TEXT("Dirt"),  FLinearColor(0.45f, 0.30f, 0.20f, 1.0f) },
        { TEXT("Rock"),  FLinearColor(0.50f, 0.50f, 0.52f, 1.0f) },
    };

    // Create LandscapeLayerBlend expression
    UMaterialExpressionLandscapeLayerBlend* LayerBlend = NewObject<UMaterialExpressionLandscapeLayerBlend>(Mat);
    LayerBlend->Layers.SetNum(4);

    // Create a constant color for each layer and connect to LayerBlend inputs
    TArray<UMaterialExpressionConstant3Vector*> ColorConsts;
    for (int32 i = 0; i < 4; i++)
    {
        UMaterialExpressionConstant3Vector* ColorConst = NewObject<UMaterialExpressionConstant3Vector>(Mat);
        ColorConst->Constant = LayerColors[i].Color;
        ColorConst->MaterialExpressionEditorX = -400;
        ColorConst->MaterialExpressionEditorY = i * 150;
        Mat->GetExpressionCollection().AddExpression(ColorConst);
        ColorConsts.Add(ColorConst);

        LayerBlend->Layers[i].LayerName = LayerColors[i].Name;
        LayerBlend->Layers[i].BlendType = LB_WeightBlend;
        LayerBlend->Layers[i].ConstLayerInput = FVector(LayerColors[i].Color.R, LayerColors[i].Color.G, LayerColors[i].Color.B);
        LayerBlend->Layers[i].LayerInput.Connect(0, ColorConst);
    }

    LayerBlend->MaterialExpressionEditorX = -100;
    LayerBlend->MaterialExpressionEditorY = 0;
    Mat->GetExpressionCollection().AddExpression(LayerBlend);

    // Connect LayerBlend output to BaseColor via EditorOnlyData
    UMaterialEditorOnlyData* EditorData = Mat->GetEditorOnlyData();
    if (EditorData)
    {
        EditorData->BaseColor.Expression = LayerBlend;
        EditorData->BaseColor.OutputIndex = 0;
    }

    // Set material properties
    Mat->BlendMode = BLEND_Opaque;
    Mat->SetShadingModel(MSM_DefaultLit);

    // Mark dirty and register
    Mat->PreEditChange(nullptr);
    Mat->PostEditChange();
    Mat->MarkPackageDirty();
    FAssetRegistryModule::AssetCreated(Mat);

    // Assign material to landscape
    Landscape->LandscapeMaterial = Mat;
    Landscape->PostEditChange();

    // Also update all components
    TArray<ULandscapeComponent*> Components;
    Landscape->GetComponents<ULandscapeComponent>(Components);
    for (ULandscapeComponent* Comp : Components)
    {
        if (Comp)
        {
            Comp->MarkRenderStateDirty();
        }
    }

    LUTE_LOG("SetupLandscapeMaterial: created M_LandscapeTerrain with 4 layers, assigned to landscape (%d components)", Components.Num());

    Json->SetBoolField("success", true);
    Json->SetStringField("material", AssetPath);
    TArray<TSharedPtr<FJsonValue>> LayerArr;
    for (int32 i = 0; i < 4; i++)
    {
        LayerArr.Add(MakeShared<FJsonValueString>(LayerColors[i].Name));
    }
    Json->SetArrayField("layers", LayerArr);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /setup_landscape_layers — create and register landscape paint layers
static bool HandleSetupLandscapeLayers(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();
    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Find the landscape actor
    ALandscape* Landscape = nullptr;
    for (TActorIterator<ALandscape> It(World); It; ++It)
    {
        Landscape = *It;
        break;
    }
    if (!Landscape)
    {
        Json->SetStringField("error", "No landscape found in level");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
    if (!LandscapeInfo)
    {
        Json->SetStringField("error", "No landscape info");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Define layers to create
    TArray<FString> LayerNames = { TEXT("Grass"), TEXT("Dirt"), TEXT("Rock"), TEXT("Sand") };

    TArray<FString> CreatedLayers;
    TArray<FString> ExistingLayers;

    for (const FString& LayerNameStr : LayerNames)
    {
        FName LayerName(*LayerNameStr);

        // Check if layer already exists
        ULandscapeLayerInfoObject* Existing = LandscapeInfo->GetLayerInfoByName(LayerName, Landscape);
        if (Existing)
        {
            ExistingLayers.Add(LayerNameStr);
            continue;
        }

        // Create a new LayerInfoObject asset
        FString AssetPath = FString::Printf(TEXT("/Game/LandscapeLayers/LS_%s"), *LayerNameStr);
        ULandscapeLayerInfoObject* LayerInfo = LoadObject<ULandscapeLayerInfoObject>(nullptr, *AssetPath);

        if (!LayerInfo)
        {
            UPackage* Package = CreatePackage(*FPaths::GetPath(AssetPath));
            if (Package)
            {
                FString AssetName = FString::Printf(TEXT("LS_%s"), *LayerNameStr);
                LayerInfo = NewObject<ULandscapeLayerInfoObject>(Package, FName(*AssetName), RF_Public | RF_Standalone);
                if (LayerInfo)
                {
                    LayerInfo->LayerName = LayerName;
                    LayerInfo->bNoWeightBlend = false;
                    Package->MarkPackageDirty();
                    FAssetRegistryModule::AssetCreated(LayerInfo);
                }
            }
        }

        if (LayerInfo)
        {
            // Register with landscape using FLandscapeInfoLayerSettings
            LandscapeInfo->Layers.Add(FLandscapeInfoLayerSettings(LayerInfo, Landscape));
            CreatedLayers.Add(LayerNameStr);
            LUTE_LOG("SetupLandscapeLayers: created layer '%s'", *LayerNameStr);
        }
        else
        {
            LUTE_LOG("SetupLandscapeLayers: FAILED to create layer '%s'", *LayerNameStr);
        }
    }

    // Mark landscape dirty so it picks up the new layers
    Landscape->Modify();

    Json->SetBoolField("success", true);
    Json->SetArrayField("created_layers", [&CreatedLayers]() {
        TArray<TSharedPtr<FJsonValue>> Arr;
        for (const FString& S : CreatedLayers)
            Arr.Add(MakeShared<FJsonValueString>(S));
        return Arr;
    }());
    Json->SetArrayField("existing_layers", [&ExistingLayers]() {
        TArray<TSharedPtr<FJsonValue>> Arr;
        for (const FString& S : ExistingLayers)
            Arr.Add(MakeShared<FJsonValueString>(S));
        return Arr;
    }());
    LUTE_LOG("SetupLandscapeLayers: created %d, existing %d", CreatedLayers.Num(), ExistingLayers.Num());
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Helper: force-load all World Partition streaming cells so we can write to all landscape components
static void ForceLoadAllWorldPartitionCells(UWorld* World)
{
    if (!World) return;
    UWorldPartition* WP = World->GetWorldPartition();
    if (!WP) return;

    LUTE_LOG("ForceLoadAllWorldPartitionCells: Loading all actors...");
    TArray<FWorldPartitionReference> LoadedRefs;
    WP->LoadAllActors(LoadedRefs);
    LUTE_LOG("ForceLoadAllWorldPartitionCells: Loaded %d references", LoadedRefs.Num());

    // Need to process pending registrations and flush rendering
    // This allows newly loaded actors to register their components
    FlushRenderingCommands();
    
    // Process pending tick/registration by running a defer
    FCoreDelegates::OnEndFrame.Broadcast();
    FlushRenderingCommands();
    
    LUTE_LOG("ForceLoadAllWorldPartitionCells: Done flushing");
}

// Helper: collect all landscape components from main landscape actor and its streaming proxies
static void GetAllLandscapeComponents(ALandscape* Landscape, TArray<ULandscapeComponent*>& OutComponents)
{
    if (!Landscape) return;
    Landscape->GetComponents<ULandscapeComponent>(OutComponents);
    int32 MainCount = OutComponents.Num();

    // Also get components from streaming proxies (World Partition landscapes)
    ULandscapeInfo* Info = Landscape->GetLandscapeInfo();
    int32 ProxyCount = 0;
    if (Info)
    {
        LUTE_LOG("GetAllLandscapeComponents: StreamingProxies.Num()=%d", Info->StreamingProxies.Num());
        for (const TWeakObjectPtr<ALandscapeStreamingProxy>& ProxyPtr : Info->StreamingProxies)
        {
            ALandscapeStreamingProxy* Proxy = ProxyPtr.Get();
            if (!Proxy) continue;
            TArray<ULandscapeComponent*> ProxyComponents;
            Proxy->GetComponents<ULandscapeComponent>(ProxyComponents);
            for (ULandscapeComponent* Comp : ProxyComponents)
            {
                if (Comp && Comp->GetHeightmap())
                {
                    OutComponents.Add(Comp);
                    ProxyCount++;
                }
            }
        }
    }
    LUTE_LOG("GetAllLandscapeComponents: main=%d, proxy=%d, total=%d", MainCount, ProxyCount, OutComponents.Num());
}

// Handle: POST /terrain_sculpt — sculpt terrain height using Landscape Edit Layers API
static bool HandleTerrainSculpt(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    double X = Body->GetNumberField(TEXT("x"));
    double Y = Body->GetNumberField(TEXT("y"));
    double Radius = Body->GetNumberField(TEXT("radius"));
    double Strength = Body->GetNumberField(TEXT("strength"));
    FString Mode = Body->GetStringField(TEXT("mode"));
    if (Mode.IsEmpty()) Mode = TEXT("raise");

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Find landscape proxy
    ALandscapeProxy* Landscape = nullptr;
    for (TActorIterator<ALandscape> It(World); It; ++It)
    {
        Landscape = *It;
        break;
    }

    if (!Landscape)
    {
        Json->SetStringField("error", "No landscape found in level. Add a Landscape actor first.");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
    if (!LandscapeInfo)
    {
        Json->SetStringField("error", "No landscape info");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Get landscape extent
    int32 MinX, MinY, MaxX, MaxY;
    LandscapeInfo->GetLandscapeExtent(MinX, MinY, MaxX, MaxY);

    // Convert world position to landscape local coordinates
    FTransform LandscapeTransform = Landscape->GetActorTransform();
    FVector LandscapeScale = LandscapeTransform.GetScale3D();
    float LandscapeZScale = FMath::IsNearlyZero(LandscapeScale.Z) ? 1.0f : LandscapeScale.Z;
    FVector LocalPos = LandscapeTransform.InverseTransformPosition(FVector(X, Y, 0));
    int32 CenterX = FMath::RoundToInt(LocalPos.X);
    int32 CenterY = FMath::RoundToInt(LocalPos.Y);

    // Calculate brush bounds in landscape quad space
    int32 BrushRadiusInt = FMath::RoundToInt(Radius);
    int32 X0 = FMath::Max(MinX, CenterX - BrushRadiusInt);
    int32 Y0 = FMath::Max(MinY, CenterY - BrushRadiusInt);
    int32 X1 = FMath::Min(MaxX, CenterX + BrushRadiusInt);
    int32 Y1 = FMath::Min(MaxY, CenterY + BrushRadiusInt);

    if (X0 >= X1 || Y0 >= Y1)
    {
        Json->SetStringField("error", "Brush outside landscape bounds");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    int32 Width = X1 - X0 + 1;
    int32 Height = Y1 - Y0 + 1;

    // Read current heightmap data using the LandscapeEdit API — must use the active edit layer
    TArray<uint16> HeightData;
    HeightData.SetNum(Width * Height);

    ALandscape* LandscapeActor = static_cast<ALandscape*>(Landscape);
    FGuid EditLayerGUID = LandscapeActor->GetEditingLayer();
    // If no edit layer is selected, find or create one
    if (!EditLayerGUID.IsValid())
    {
        TArray<const ULandscapeEditLayerBase*> EditLayers = LandscapeActor->GetEditLayersConst();
        if (EditLayers.Num() > 0)
        {
            EditLayerGUID = EditLayers[0]->GetGuid();
            LandscapeActor->SetEditingLayer(EditLayerGUID);
            LUTE_LOG("TerrainSculpt: Auto-selected edit layer %s", *EditLayerGUID.ToString());
        }
        else
        {
            int32 NewLayerIdx = LandscapeActor->CreateLayer(TEXT("AutoLayer"));
            TArrayView<const FLandscapeLayer> Layers = LandscapeActor->GetLayersConst();
            if (Layers.Num() > NewLayerIdx)
            {
                EditLayerGUID = Layers[NewLayerIdx].EditLayer->GetGuid();
                LandscapeActor->SetEditingLayer(EditLayerGUID);
                LUTE_LOG("TerrainSculpt: Created edit layer %s", *EditLayerGUID.ToString());
            }
        }
    }
    FLandscapeEditDataInterface LandscapeEdit(LandscapeInfo, EditLayerGUID);
    int32 ReadX0 = X0, ReadY0 = Y0, ReadX1 = X1, ReadY1 = Y1;
    LandscapeEdit.GetHeightDataFast(ReadX0, ReadY0, ReadX1, ReadY1, HeightData.GetData(), 0);

    // Modify height data based on mode
    int32 AffectedVerts = 0;
    for (int32 vy = 0; vy < Height; vy++)
    {
        for (int32 vx = 0; vx < Width; vx++)
        {
            int32 GlobalX = X0 + vx;
            int32 GlobalY = Y0 + vy;
            float Dist = FMath::Sqrt((float)(GlobalX - CenterX) * (GlobalX - CenterX) + (float)(GlobalY - CenterY) * (GlobalY - CenterY));

            if (Dist > Radius) continue;

            float Falloff = 1.0f - (Dist / Radius);
            Falloff = Falloff * Falloff;

            int32 Idx = vy * Width + vx;
            float CurrentHeightF = (float)HeightData[Idx] - 32768.0f;
            float Delta = Strength * Falloff;

            if (Mode == TEXT("raise"))
            {
                CurrentHeightF += Delta;
            }
            else if (Mode == TEXT("lower"))
            {
                CurrentHeightF -= Delta;
            }
            else if (Mode == TEXT("flatten"))
            {
                CurrentHeightF = FMath::Lerp(CurrentHeightF, 0.0f, Falloff * 0.5f);
            }
            else if (Mode == TEXT("smooth"))
            {
                float Avg = CurrentHeightF;
                int32 Count = 1;
                if (vx > 0) { Avg += (float)HeightData[Idx - 1] - 32768.0f; Count++; }
                if (vx < Width - 1) { Avg += (float)HeightData[Idx + 1] - 32768.0f; Count++; }
                if (vy > 0) { Avg += (float)HeightData[Idx - Width] - 32768.0f; Count++; }
                if (vy < Height - 1) { Avg += (float)HeightData[Idx + Width] - 32768.0f; Count++; }
                Avg /= Count;
                CurrentHeightF = FMath::Lerp(CurrentHeightF, Avg, Falloff * 0.5f);
            }
            else if (Mode == TEXT("set"))
            {
                // strength = absolute target height in UE world units.
                // Raw heightmap deviation = (WorldZ / ActorZScale) * 128, since the
                // engine's LocalHeight = (RawHeight-32768)/128, WorldZ = LocalHeight*ActorZScale.
                float TargetLandscapeUnits = ((float)Strength / LandscapeZScale) * 128.0f;
                CurrentHeightF = FMath::Lerp(CurrentHeightF, TargetLandscapeUnits, Falloff);
            }

            CurrentHeightF = FMath::Clamp(CurrentHeightF, -32768.0f, 32767.0f);
            HeightData[Idx] = (uint16)(CurrentHeightF + 32768.0f);
            AffectedVerts++;
        }
    }

    // Write modified height data back using the LandscapeEdit API.
    // SetHeightData writes into each component's editable Texture Source data and
    // updates collision; no manual texture write is needed (nor safe — PlatformData
    // Mips are the derived runtime GPU data, not the editable source).
    LandscapeEdit.SetHeightData(X0, Y0, X1, Y1, HeightData.GetData(), 0, true);

    // Trigger a FULL edit layer merge across ALL components (including unloaded
    // World Partition streaming proxies), not just currently visible/dirty ones.
    static_cast<ALandscape*>(Landscape)->ForceLayersFullUpdate();
    FlushRenderingCommands();

    Json->SetBoolField("success", true);
    Json->SetNumberField("affected_verts", AffectedVerts);
    Json->SetStringField("mode", Mode);
    LUTE_LOG("TerrainSculpt(EditLayers): mode=%s at (%.1f, %.1f) radius=%.1f strength=%.1f verts=%d",
        *Mode, X, Y, Radius, Strength, AffectedVerts);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: GET /landscape_info — returns landscape dimensions and transform
static bool HandleLandscapeInfo(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    ALandscape* Landscape = nullptr;
    for (TActorIterator<ALandscape> It(World); It; ++It)
    {
        Landscape = *It;
        break;
    }
    if (!Landscape)
    {
        Json->SetStringField("error", "No landscape found");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
    if (!LandscapeInfo)
    {
        Json->SetStringField("error", "No landscape info");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    int32 MinX, MinY, MaxX, MaxY;
    LandscapeInfo->GetLandscapeExtent(MinX, MinY, MaxX, MaxY);

    FTransform LandscapeTransform = Landscape->GetActorTransform();
    FVector LandscapeScale = LandscapeTransform.GetScale3D();
    FVector LandscapePos = LandscapeTransform.GetLocation();

    // Convert landscape local extent to world coordinates
    FVector WorldMin = LandscapeTransform.TransformPosition(FVector(MinX, MinY, 0));
    FVector WorldMax = LandscapeTransform.TransformPosition(FVector(MaxX, MaxY, 0));

    Json->SetBoolField("success", true);
    Json->SetNumberField("min_x", MinX);
    Json->SetNumberField("min_y", MinY);
    Json->SetNumberField("max_x", MaxX);
    Json->SetNumberField("max_y", MaxY);
    Json->SetNumberField("world_min_x", WorldMin.X);
    Json->SetNumberField("world_min_y", WorldMin.Y);
    Json->SetNumberField("world_max_x", WorldMax.X);
    Json->SetNumberField("world_max_y", WorldMax.Y);
    Json->SetNumberField("scale_x", LandscapeScale.X);
    Json->SetNumberField("scale_y", LandscapeScale.Y);
    Json->SetNumberField("scale_z", LandscapeScale.Z);
    Json->SetNumberField("pos_x", LandscapePos.X);
    Json->SetNumberField("pos_y", LandscapePos.Y);
    Json->SetNumberField("pos_z", LandscapePos.Z);
    Json->SetNumberField("grid_size", MaxX - MinX + 1);
    Json->SetNumberField("grid_height", MaxY - MinY + 1);

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /set_heightmap — bulk write entire heightmap from a base64-encoded uint16 array
static bool HandleSetHeightmap(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString B64Data = Body->GetStringField(TEXT("data"));
    int32 DataWidth = (int32)Body->GetNumberField(TEXT("width"));
    int32 DataHeight = (int32)Body->GetNumberField(TEXT("height"));
    // World bounds — if not provided, will default to full landscape extent
    bool bHasBounds = Body->HasField(TEXT("x0")) && Body->HasField(TEXT("x1"));
    double WorldX0 = Body->HasField(TEXT("x0")) ? Body->GetNumberField(TEXT("x0")) : 0.0;
    double WorldY0 = Body->HasField(TEXT("y0")) ? Body->GetNumberField(TEXT("y0")) : 0.0;
    double WorldX1 = Body->HasField(TEXT("x1")) ? Body->GetNumberField(TEXT("x1")) : 0.0;
    double WorldY1 = Body->HasField(TEXT("y1")) ? Body->GetNumberField(TEXT("y1")) : 0.0;
    // uint16 mode: data is uint16 values mapped through min_z/max_z
    double MinZ = Body->HasField(TEXT("min_z")) ? Body->GetNumberField(TEXT("min_z")) : 0.0;
    double MaxZ = Body->HasField(TEXT("max_z")) ? Body->GetNumberField(TEXT("max_z")) : 0.0;
    bool bHasMinMaxZ = Body->HasField(TEXT("min_z")) && Body->HasField(TEXT("max_z"));

    if (B64Data.IsEmpty() || DataWidth <= 0 || DataHeight <= 0)
    {
        Json->SetStringField("error", "Missing or invalid 'data', 'width', or 'height'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Find landscape
    ALandscape* Landscape = nullptr;
    for (TActorIterator<ALandscape> It(World); It; ++It)
    {
        Landscape = *It;
        break;
    }
    if (!Landscape)
    {
        Json->SetStringField("error", "No landscape found");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
    if (!LandscapeInfo)
    {
        Json->SetStringField("error", "No landscape info");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Get landscape extent and transform
    int32 MinX, MinY, MaxX, MaxY;
    LandscapeInfo->GetLandscapeExtent(MinX, MinY, MaxX, MaxY);

    FTransform LandscapeTransform = Landscape->GetActorTransform();
    FVector LandscapeScale = LandscapeTransform.GetScale3D();
    float LandscapeZScale = FMath::IsNearlyZero(LandscapeScale.Z) ? 1.0f : LandscapeScale.Z;

    // Decode base64 to raw bytes
    TArray<uint8> DecodedBytes;
    FBase64::Decode(B64Data, DecodedBytes);

    // Determine data format: uint16 (2 bytes) if min_z/max_z provided, else float32 (4 bytes)
    bool bIsUint16 = bHasMinMaxZ;
    int32 BytesPerElement = bIsUint16 ? 2 : 4;
    int32 ExpectedBytes = DataWidth * DataHeight * BytesPerElement;

    if (DecodedBytes.Num() < ExpectedBytes)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Data too short: got %d bytes, expected %d"), DecodedBytes.Num(), ExpectedBytes));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    const uint16* HeightUint16 = reinterpret_cast<const uint16*>(DecodedBytes.GetData());
    const float* HeightFloats = reinterpret_cast<const float*>(DecodedBytes.GetData());

    // If no world bounds provided, default to full landscape extent
    if (!bHasBounds)
    {
        FVector WorldMin = LandscapeTransform.TransformPosition(FVector(MinX, MinY, 0));
        FVector WorldMax = LandscapeTransform.TransformPosition(FVector(MaxX, MaxY, 0));
        WorldX0 = WorldMin.X;
        WorldY0 = WorldMin.Y;
        WorldX1 = WorldMax.X;
        WorldY1 = WorldMax.Y;
    }

    // Convert world bounds to landscape local coordinates
    FVector LocalOrigin = LandscapeTransform.InverseTransformPosition(FVector(WorldX0, WorldY0, 0));
    FVector LocalExtent = LandscapeTransform.InverseTransformPosition(FVector(WorldX1, WorldY1, 0));

    int32 LandX0 = FMath::Max(MinX, FMath::RoundToInt(LocalOrigin.X));
    int32 LandY0 = FMath::Max(MinY, FMath::RoundToInt(LocalOrigin.Y));
    int32 LandX1 = FMath::Min(MaxX, FMath::RoundToInt(LocalExtent.X));
    int32 LandY1 = FMath::Min(MaxY, FMath::RoundToInt(LocalExtent.Y));

    int32 LandWidth = LandX1 - LandX0 + 1;
    int32 LandHeight = LandY1 - LandY0 + 1;

    if (LandWidth <= 0 || LandHeight <= 0)
    {
        Json->SetStringField("error", "World bounds outside landscape extent");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Build the heightmap array for the landscape region
    TArray<uint16> HeightData;
    HeightData.SetNum(LandWidth * LandHeight);

    float WorldWidthRange = WorldX1 - WorldX0;
    float WorldHeightRange = WorldY1 - WorldY0;

    int32 WrittenVerts = 0;
    for (int32 ly = 0; ly < LandHeight; ly++)
    {
        for (int32 lx = 0; lx < LandWidth; lx++)
        {
            // Map landscape coordinate to data array coordinate
            int32 GlobalX = LandX0 + lx;
            int32 GlobalY = LandY0 + ly;

            // Convert back to world coords for sampling
            FVector WorldPos = LandscapeTransform.TransformPosition(FVector(GlobalX, GlobalY, 0));
            float SampleX = (WorldPos.X - WorldX0) / WorldWidthRange;
            float SampleY = (WorldPos.Y - WorldY0) / WorldHeightRange;

            // Clamp and convert to array indices
            int32 DataX = FMath::Clamp(FMath::RoundToInt(SampleX * (DataWidth - 1)), 0, DataWidth - 1);
            int32 DataY = FMath::Clamp(FMath::RoundToInt(SampleY * (DataHeight - 1)), 0, DataHeight - 1);

            // Get world Z height from data array
            float WorldZ;
            if (bIsUint16)
            {
                // Map uint16 [0..65535] to world Z [MinZ..MaxZ]
                float Normalized = (float)HeightUint16[DataY * DataWidth + DataX] / 65535.0f;
                WorldZ = MinZ + Normalized * (MaxZ - MinZ);
            }
            else
            {
                WorldZ = HeightFloats[DataY * DataWidth + DataX];
            }

            // Convert to landscape heightmap units. The engine's own formula is:
            // LocalHeight = (RawHeight-32768)/128, WorldZ = LocalHeight * ActorZScale.
            // So: RawHeightDeviation = (WorldZ / ActorZScale) * 128.
            float LandscapeUnits = (WorldZ / LandscapeZScale) * 128.0f;
            LandscapeUnits = FMath::Clamp(LandscapeUnits, -32768.0f, 32767.0f);

            HeightData[ly * LandWidth + lx] = (uint16)(LandscapeUnits + 32768.0f);
            WrittenVerts++;
        }
    }

    // Write all height data — must use the active edit layer
    FGuid EditLayerGUID = Landscape->GetEditingLayer();
    // If no edit layer is selected, find or create one
    if (!EditLayerGUID.IsValid())
    {
        TArray<const ULandscapeEditLayerBase*> EditLayers = Landscape->GetEditLayersConst();
        if (EditLayers.Num() > 0)
        {
            EditLayerGUID = EditLayers[0]->GetGuid();
            Landscape->SetEditingLayer(EditLayerGUID);
            LUTE_LOG("SetHeightmap: Auto-selected edit layer %s", *EditLayerGUID.ToString());
        }
        else
        {
            int32 NewLayerIdx = Landscape->CreateLayer(TEXT("AutoLayer"));
            TArrayView<const FLandscapeLayer> Layers = Landscape->GetLayersConst();
            if (Layers.Num() > NewLayerIdx)
            {
                EditLayerGUID = Layers[NewLayerIdx].EditLayer->GetGuid();
                Landscape->SetEditingLayer(EditLayerGUID);
                LUTE_LOG("SetHeightmap: Created edit layer %s", *EditLayerGUID.ToString());
            }
        }
    }
    LUTE_LOG("SetHeightmap: EditLayerGUID=%s, LandscapeZScale=%.1f, LandRegion=(%d,%d)-(%d,%d) %dx%d",
        *EditLayerGUID.ToString(), LandscapeZScale, LandX0, LandY0, LandX1, LandY1, LandWidth, LandHeight);
    LUTE_LOG("SetHeightmap: Sample heights [0]=%d [1]=%d [mid]=%d [last]=%d (uint16, 32768=sea level)",
        HeightData[0], HeightData[1], HeightData[LandWidth * LandHeight / 2], HeightData[LandWidth * LandHeight - 1]);
    FLandscapeEditDataInterface LandscapeEdit(LandscapeInfo, EditLayerGUID);
    LandscapeEdit.SetHeightData(LandX0, LandY0, LandX1, LandY1, HeightData.GetData(), 0, true);

    // SetHeightData already writes into each component's editable Texture Source data
    // and updates collision. Trigger a FULL edit layer merge across ALL components
    // (including unloaded World Partition streaming proxies), not just currently
    // visible/dirty ones.
    Landscape->ForceLayersFullUpdate();
    FlushRenderingCommands();

    Landscape->MarkPackageDirty();

    Json->SetBoolField("success", true);
    Json->SetNumberField("written_verts", WrittenVerts);
    Json->SetNumberField("land_extent_x", LandX0);
    Json->SetNumberField("land_extent_y", LandY0);
    Json->SetNumberField("land_width", LandWidth);
    Json->SetNumberField("land_height", LandHeight);
    LUTE_LOG("SetHeightmap: wrote %d verts (%dx%d) from %dx%d data array",
        WrittenVerts, LandWidth, LandHeight, DataWidth, DataHeight);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: GET /get_terrain_height — query terrain height at a world position
static bool HandleGetTerrainHeight(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Find landscape
    ALandscape* Landscape = nullptr;
    for (TActorIterator<ALandscape> It(World); It; ++It)
    {
        Landscape = *It;
        break;
    }

    if (!Landscape)
    {
        Json->SetBoolField("has_landscape", false);
        Json->SetNumberField("height", 0.0);
        Callback(MakeJsonResponse(JsonToString(Json)));
        return true;
    }

    // Use collision trace to find height
    FVector Start(0, 0, 100000);
    FVector End(0, 0, -100000);

    // Parse query params — Request.QueryParams is already parsed by the HTTP server
    double QueryX = 0, QueryY = 0;
    const FString* XVal = Request.QueryParams.Find(TEXT("x"));
    const FString* YVal = Request.QueryParams.Find(TEXT("y"));
    if (XVal) QueryX = FCString::Atod(**XVal);
    if (YVal) QueryY = FCString::Atod(**YVal);

    Start.X = QueryX;
    Start.Y = QueryY;
    End.X = QueryX;
    End.Y = QueryY;

    FHitResult HitResult;
    FCollisionQueryParams TraceParams;
    TraceParams.bTraceComplex = true;

    bool bHit = World->LineTraceSingleByChannel(HitResult, Start, End, ECC_Visibility, TraceParams);

    Json->SetBoolField("has_landscape", true);
    Json->SetBoolField("hit", bHit);
    if (bHit)
    {
        Json->SetNumberField("height", HitResult.ImpactPoint.Z);
        Json->SetNumberField("x", HitResult.ImpactPoint.X);
        Json->SetNumberField("y", HitResult.ImpactPoint.Y);
    }
    else
    {
        // Fallback: read heightmap data directly from the landscape info
        ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
        if (LandscapeInfo)
        {
            int32 MinX, MinY, MaxX, MaxY;
            LandscapeInfo->GetLandscapeExtent(MinX, MinY, MaxX, MaxY);

            FTransform LandscapeTransform = Landscape->GetActorTransform();
            FVector LandscapeScale = LandscapeTransform.GetScale3D();
            float LandscapeZScale = FMath::IsNearlyZero(LandscapeScale.Z) ? 1.0f : LandscapeScale.Z;

            // Convert world position to landscape local coordinates
            FVector LocalPos = LandscapeTransform.InverseTransformPosition(FVector(QueryX, QueryY, 0));
            int32 LocalX = FMath::RoundToInt(LocalPos.X);
            int32 LocalY = FMath::RoundToInt(LocalPos.Y);

            if (LocalX >= MinX && LocalX <= MaxX && LocalY >= MinY && LocalY <= MaxY)
            {
                // Read single vertex from heightmap — use active edit layer
                uint16 HeightValue = 0;
                FGuid EditLayerGUID = Landscape->GetEditingLayer();
                FLandscapeEditDataInterface LandscapeEdit(LandscapeInfo, EditLayerGUID);
                int32 ReadX0 = LocalX, ReadY0 = LocalY, ReadX1 = LocalX, ReadY1 = LocalY;
                LandscapeEdit.GetHeightDataFast(ReadX0, ReadY0, ReadX1, ReadY1, &HeightValue, 0);

                float LandscapeUnits = (float)HeightValue - 32768.0f;
                float WorldZ = (LandscapeUnits / 128.0f) * LandscapeZScale;

                Json->SetBoolField("hit", true);
                Json->SetNumberField("height", WorldZ);
                Json->SetNumberField("x", QueryX);
                Json->SetNumberField("y", QueryY);
                Json->SetBoolField("from_heightmap", true);
            }
            else
            {
                Json->SetNumberField("height", 0.0);
            }
        }
        else
        {
            Json->SetNumberField("height", 0.0);
        }
    }

    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /batch_terrain_height — query terrain height for many points in one request
static bool HandleBatchTerrainHeight(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    const TArray<TSharedPtr<FJsonValue>>* Points = nullptr;
    if (!Body->TryGetArrayField(TEXT("points"), Points))
    {
        Json->SetStringField("error", "Missing 'points' array");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    ALandscape* Landscape = nullptr;
    for (TActorIterator<ALandscape> It(World); It; ++It)
    {
        Landscape = *It;
        break;
    }

    if (!Landscape)
    {
        Json->SetStringField("error", "No landscape found");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
    if (!LandscapeInfo)
    {
        Json->SetStringField("error", "No landscape info");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    int32 MinX, MinY, MaxX, MaxY;
    LandscapeInfo->GetLandscapeExtent(MinX, MinY, MaxX, MaxY);

    FTransform LandscapeTransform = Landscape->GetActorTransform();
    FVector LandscapeScale = LandscapeTransform.GetScale3D();
    float LandscapeZScale = FMath::IsNearlyZero(LandscapeScale.Z) ? 1.0f : LandscapeScale.Z;

    FGuid EditLayerGUID = Landscape->GetEditingLayer();
    FLandscapeEditDataInterface LandscapeEdit(LandscapeInfo, EditLayerGUID);

    TArray<TSharedPtr<FJsonValue>> Results;
    int32 SuccessCount = 0;

    for (const TSharedPtr<FJsonValue>& PointVal : *Points)
    {
        TSharedPtr<FJsonObject> P = PointVal->AsObject();
        if (!P.IsValid()) continue;

        double QX = P->GetNumberField(TEXT("x"));
        double QY = P->GetNumberField(TEXT("y"));

        TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
        Result->SetNumberField("x", QX);
        Result->SetNumberField("y", QY);

        FVector LocalPos = LandscapeTransform.InverseTransformPosition(FVector(QX, QY, 0));
        int32 LocalX = FMath::RoundToInt(LocalPos.X);
        int32 LocalY = FMath::RoundToInt(LocalPos.Y);

        if (LocalX >= MinX && LocalX <= MaxX && LocalY >= MinY && LocalY <= MaxY)
        {
            uint16 HeightValue = 0;
            int32 ReadX0 = LocalX, ReadY0 = LocalY, ReadX1 = LocalX, ReadY1 = LocalY;
            LandscapeEdit.GetHeightDataFast(ReadX0, ReadY0, ReadX1, ReadY1, &HeightValue, 0);

            float LandscapeUnits = (float)HeightValue - 32768.0f;
            float WorldZ = (LandscapeUnits / 128.0f) * LandscapeZScale;

            Result->SetNumberField("z", WorldZ);
            Result->SetBoolField("hit", true);
            SuccessCount++;
        }
        else
        {
            Result->SetNumberField("z", 0.0);
            Result->SetBoolField("hit", false);
        }

        Results.Add(MakeShared<FJsonValueObject>(Result));
    }

    Json->SetBoolField("success", true);
    Json->SetNumberField("count", SuccessCount);
    Json->SetArrayField("heights", Results);

    LUTE_LOG("BatchTerrainHeight: queried %d points, %d hits", Points->Num(), SuccessCount);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /set_layer_weightmap — bulk write an entire layer weightmap from base64 uint8 array
// Same pattern as /set_heightmap: one call writes the whole layer, no brush strokes
static bool HandleSetLayerWeightmap(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    FString B64Data = Body->GetStringField(TEXT("data"));
    int32 DataWidth = (int32)Body->GetNumberField(TEXT("width"));
    int32 DataHeight = (int32)Body->GetNumberField(TEXT("height"));
    FString LayerName = Body->GetStringField(TEXT("layer"));

    // World bounds — if not provided, default to full landscape extent
    bool bHasBounds = Body->HasField(TEXT("x0")) && Body->HasField(TEXT("x1"));
    double WorldX0 = Body->HasField(TEXT("x0")) ? Body->GetNumberField(TEXT("x0")) : 0.0;
    double WorldY0 = Body->HasField(TEXT("y0")) ? Body->GetNumberField(TEXT("y0")) : 0.0;
    double WorldX1 = Body->HasField(TEXT("x1")) ? Body->GetNumberField(TEXT("x1")) : 0.0;
    double WorldY1 = Body->HasField(TEXT("y1")) ? Body->GetNumberField(TEXT("y1")) : 0.0;

    if (B64Data.IsEmpty() || DataWidth <= 0 || DataHeight <= 0 || LayerName.IsEmpty())
    {
        Json->SetStringField("error", "Missing or invalid 'data', 'width', 'height', or 'layer'");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    ALandscape* Landscape = nullptr;
    for (TActorIterator<ALandscape> It(World); It; ++It)
    {
        Landscape = *It;
        break;
    }
    if (!Landscape)
    {
        Json->SetStringField("error", "No landscape found");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
    if (!LandscapeInfo)
    {
        Json->SetStringField("error", "No landscape info");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Find the layer info object by name
    ULandscapeLayerInfoObject* LayerInfo = LandscapeInfo->GetLayerInfoByName(FName(*LayerName), Landscape);
    if (!LayerInfo)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Layer '%s' not found on landscape"), *LayerName));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Get landscape extent and transform
    int32 MinX, MinY, MaxX, MaxY;
    LandscapeInfo->GetLandscapeExtent(MinX, MinY, MaxX, MaxY);
    FTransform LandscapeTransform = Landscape->GetActorTransform();

    // Decode base64 to raw bytes (uint8 weight data, 0-255)
    TArray<uint8> DecodedBytes;
    FBase64::Decode(B64Data, DecodedBytes);

    int32 ExpectedBytes = DataWidth * DataHeight;
    if (DecodedBytes.Num() < ExpectedBytes)
    {
        Json->SetStringField("error", FString::Printf(TEXT("Data too short: got %d bytes, expected %d"), DecodedBytes.Num(), ExpectedBytes));
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // If no world bounds provided, default to full landscape extent
    if (!bHasBounds)
    {
        FVector WorldMin = LandscapeTransform.TransformPosition(FVector(MinX, MinY, 0));
        FVector WorldMax = LandscapeTransform.TransformPosition(FVector(MaxX, MaxY, 0));
        WorldX0 = WorldMin.X;
        WorldY0 = WorldMin.Y;
        WorldX1 = WorldMax.X;
        WorldY1 = WorldMax.Y;
    }

    // Convert world bounds to landscape local coordinates
    FVector LocalOrigin = LandscapeTransform.InverseTransformPosition(FVector(WorldX0, WorldY0, 0));
    FVector LocalExtent = LandscapeTransform.InverseTransformPosition(FVector(WorldX1, WorldY1, 0));

    int32 LandX0 = FMath::Max(MinX, FMath::RoundToInt(LocalOrigin.X));
    int32 LandY0 = FMath::Max(MinY, FMath::RoundToInt(LocalOrigin.Y));
    int32 LandX1 = FMath::Min(MaxX, FMath::RoundToInt(LocalExtent.X));
    int32 LandY1 = FMath::Min(MaxY, FMath::RoundToInt(LocalExtent.Y));

    int32 LandWidth = LandX1 - LandX0 + 1;
    int32 LandHeight = LandY1 - LandY0 + 1;

    if (LandWidth <= 0 || LandHeight <= 0)
    {
        Json->SetStringField("error", "World bounds outside landscape extent");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Build the weightmap array for the landscape region by sampling from the data array
    TArray<uint8> WeightData;
    WeightData.SetNum(LandWidth * LandHeight);

    float WorldWidthRange = WorldX1 - WorldX0;
    float WorldHeightRange = WorldY1 - WorldY0;
    int32 WrittenVerts = 0;

    for (int32 ly = 0; ly < LandHeight; ly++)
    {
        for (int32 lx = 0; lx < LandWidth; lx++)
        {
            int32 GlobalX = LandX0 + lx;
            int32 GlobalY = LandY0 + ly;

            FVector WorldPos = LandscapeTransform.TransformPosition(FVector(GlobalX, GlobalY, 0));
            float SampleX = (WorldPos.X - WorldX0) / WorldWidthRange;
            float SampleY = (WorldPos.Y - WorldY0) / WorldHeightRange;

            int32 DataX = FMath::Clamp(FMath::RoundToInt(SampleX * (DataWidth - 1)), 0, DataWidth - 1);
            int32 DataY = FMath::Clamp(FMath::RoundToInt(SampleY * (DataHeight - 1)), 0, DataHeight - 1);

            WeightData[ly * LandWidth + lx] = DecodedBytes[DataY * DataWidth + DataX];
            WrittenVerts++;
        }
    }

    // Get or create edit layer
    FGuid EditLayerGUID = Landscape->GetEditingLayer();
    if (!EditLayerGUID.IsValid())
    {
        TArray<const ULandscapeEditLayerBase*> EditLayers = Landscape->GetEditLayersConst();
        if (EditLayers.Num() > 0)
        {
            EditLayerGUID = EditLayers[0]->GetGuid();
            Landscape->SetEditingLayer(EditLayerGUID);
        }
        else
        {
            int32 NewLayerIdx = Landscape->CreateLayer(TEXT("AutoLayer"));
            TArrayView<const FLandscapeLayer> Layers = Landscape->GetLayersConst();
            if (Layers.Num() > NewLayerIdx)
            {
                EditLayerGUID = Layers[NewLayerIdx].EditLayer->GetGuid();
                Landscape->SetEditingLayer(EditLayerGUID);
            }
        }
    }

    FLandscapeEditDataInterface LandscapeEdit(LandscapeInfo, EditLayerGUID);
    LandscapeEdit.SetAlphaData(LayerInfo, LandX0, LandY0, LandX1, LandY1, WeightData.GetData(), 0);

    // Note: ForceLayersFullUpdate is deferred to /flush_landscape to avoid 4x heavy flushes
    Landscape->MarkPackageDirty();

    Json->SetBoolField("success", true);
    Json->SetNumberField("written_verts", WrittenVerts);
    Json->SetStringField("layer", LayerName);
    LUTE_LOG("SetLayerWeightmap: layer '%s' wrote %d verts (%dx%d) from %dx%d data",
        *LayerName, WrittenVerts, LandWidth, LandHeight, DataWidth, DataHeight);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /flush_landscape — flush all pending landscape edits (call once after multiple layer uploads)
static bool HandleFlushLandscape(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    ALandscape* Landscape = nullptr;
    for (TActorIterator<ALandscape> It(World); It; ++It)
    {
        Landscape = *It;
        break;
    }
    if (!Landscape)
    {
        Json->SetStringField("error", "No landscape found");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    Landscape->ForceLayersFullUpdate();
    FlushRenderingCommands();
    Landscape->MarkPackageDirty();

    Json->SetBoolField("success", true);
    LUTE_LOG("FlushLandscape: forced full update + flush");
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /terrain_paint — paint a landscape layer using Edit Layers API
static bool HandleTerrainPaint(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Body = ParseJsonBody(Request);
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();

    if (!Body.IsValid())
    {
        Json->SetStringField("error", "Invalid JSON body");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    double X = Body->GetNumberField(TEXT("x"));
    double Y = Body->GetNumberField(TEXT("y"));
    double Radius = Body->GetNumberField(TEXT("radius"));
    double Strength = Body->GetNumberField(TEXT("strength"));
    FString LayerName = Body->GetStringField(TEXT("layer"));

    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    ALandscapeProxy* Landscape = nullptr;
    for (TActorIterator<ALandscape> It(World); It; ++It)
    {
        Landscape = *It;
        break;
    }

    if (!Landscape)
    {
        Json->SetStringField("error", "No landscape found in level");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
    if (!LandscapeInfo)
    {
        Json->SetStringField("error", "No landscape info");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    int32 MinX, MinY, MaxX, MaxY;
    LandscapeInfo->GetLandscapeExtent(MinX, MinY, MaxX, MaxY);

    FTransform LandscapeTransform = Landscape->GetActorTransform();
    FVector LocalPos = LandscapeTransform.InverseTransformPosition(FVector(X, Y, 0));
    int32 CenterX = FMath::RoundToInt(LocalPos.X);
    int32 CenterY = FMath::RoundToInt(LocalPos.Y);

    int32 BrushRadiusInt = FMath::RoundToInt(Radius);
    int32 X0 = FMath::Max(MinX, CenterX - BrushRadiusInt);
    int32 Y0 = FMath::Max(MinY, CenterY - BrushRadiusInt);
    int32 X1 = FMath::Min(MaxX, CenterX + BrushRadiusInt);
    int32 Y1 = FMath::Min(MaxY, CenterY + BrushRadiusInt);

    if (X0 >= X1 || Y0 >= Y1)
    {
        Json->SetStringField("error", "Brush outside landscape bounds");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    int32 Width = X1 - X0 + 1;
    int32 Height = Y1 - Y0 + 1;
    int32 DataSize = Width * Height;
    TArray<uint8> WeightData;
    WeightData.SetNum(DataSize);

    // Use FLandscapeEditDataInterface for weight data access — must use active edit layer
    ALandscape* LandscapeActor = static_cast<ALandscape*>(Landscape);
    FGuid EditLayerGUID = LandscapeActor->GetEditingLayer();
    if (!EditLayerGUID.IsValid())
    {
        TArray<const ULandscapeEditLayerBase*> EditLayers = LandscapeActor->GetEditLayersConst();
        if (EditLayers.Num() > 0)
        {
            EditLayerGUID = EditLayers[0]->GetGuid();
            LandscapeActor->SetEditingLayer(EditLayerGUID);
        }
        else
        {
            int32 NewLayerIdx = LandscapeActor->CreateLayer(TEXT("AutoLayer"));
            TArrayView<const FLandscapeLayer> Layers = LandscapeActor->GetLayersConst();
            if (Layers.Num() > NewLayerIdx)
            {
                EditLayerGUID = Layers[NewLayerIdx].EditLayer->GetGuid();
                LandscapeActor->SetEditingLayer(EditLayerGUID);
            }
        }
    }
    FLandscapeEditDataInterface LandscapeEdit(LandscapeInfo, EditLayerGUID);

    // Find the layer info object by name
    ULandscapeLayerInfoObject* LayerInfo = LandscapeInfo->GetLayerInfoByName(FName(*LayerName), Landscape);
    if (!LayerInfo)
    {
        // Try to find any available layer
        TArray<ULandscapeLayerInfoObject*> UsedLayers;
        LandscapeInfo->GetUsedPaintLayers(FGuid(), UsedLayers);
        if (UsedLayers.Num() > 0)
        {
            LayerInfo = UsedLayers[0];
        }
    }

    if (!LayerInfo)
    {
        Json->SetStringField("error", "No landscape layers found. Add a layer to the landscape first.");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::BadRequest));
        return true;
    }

    // Read current weight data for the layer
    int32 ReadX0 = X0, ReadY0 = Y0, ReadX1 = X1, ReadY1 = Y1;
    LandscapeEdit.GetWeightDataFast(LayerInfo, ReadX0, ReadY0, ReadX1, ReadY1, WeightData.GetData(), 0);

    int32 AffectedVerts = 0;
    for (int32 vy = 0; vy < Height; vy++)
    {
        for (int32 vx = 0; vx < Width; vx++)
        {
            int32 GlobalX = X0 + vx;
            int32 GlobalY = Y0 + vy;
            float Dist = FMath::Sqrt((float)(GlobalX - CenterX) * (GlobalX - CenterX) + (float)(GlobalY - CenterY) * (GlobalY - CenterY));

            if (Dist > Radius) continue;

            float Falloff = 1.0f - (Dist / Radius);
            Falloff = Falloff * Falloff;

            int32 Idx = vy * Width + vx;
            float PaintAmount = Strength * Falloff * 255.0f;
            WeightData[Idx] = FMath::Clamp((int32)(WeightData[Idx] + PaintAmount), 0, 255);
            AffectedVerts++;
        }
    }

    // Write modified weight data back using SetAlphaData.
    // SetAlphaData writes into each component's editable Texture Source data;
    // no manual texture write is needed (nor safe — PlatformData Mips are the
    // derived runtime GPU data, not the editable source).
    LandscapeEdit.SetAlphaData(LayerInfo, X0, Y0, X1, Y1, WeightData.GetData(), 0);

    // Trigger a FULL edit layer merge across ALL components (including unloaded
    // World Partition streaming proxies), not just currently visible/dirty ones.
    LandscapeActor->ForceLayersFullUpdate();
    FlushRenderingCommands();

    Json->SetBoolField("success", true);
    Json->SetNumberField("affected_verts", AffectedVerts);
    Json->SetStringField("layer", LayerName);
    LUTE_LOG("TerrainPaint(EditLayers): layer=%s at (%.1f, %.1f) radius=%.1f verts=%d",
        *LayerName, X, Y, Radius, AffectedVerts);
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

// Handle: POST /spawn_player — start PIE then spawn MagePlayer and possess
static bool HandleSpawnPlayer(const FHttpServerRequest& Request, const FResponseCallback& Callback)
{
    TSharedPtr<FJsonObject> Json = MakeShared<FJsonObject>();
    UWorld* World = GetActiveWorld();
    if (!World)
    {
        Json->SetStringField("error", "No active world");
        Callback(MakeJsonResponse(JsonToString(Json), EHttpServerResponseCodes::ServerError));
        return true;
    }

    // Ensure there's a PlayerStart in the level
    bool bHasPlayerStart = false;
    for (TActorIterator<APlayerStart> It(World); It; ++It)
    {
        bHasPlayerStart = true;
        break;
    }
    if (!bHasPlayerStart)
    {
        FActorSpawnParameters SpawnParams;
        APlayerStart* PS = World->SpawnActor<APlayerStart>(FVector(0, 0, 1000), FRotator::ZeroRotator, SpawnParams);
        if (PS)
        {
            LUTE_LOG("SpawnPlayer: Created PlayerStart at (0,0,1000)");
        }
    }

    // Start PIE
    if (GEditor)
    {
        FRequestPlaySessionParams PlayParams;
        PlayParams.SessionDestination = EPlaySessionDestinationType::InProcess;
        PlayParams.WorldType = EPlaySessionWorldType::PlayInEditor;
        GEditor->RequestPlaySession(PlayParams);
        LUTE_LOG("SpawnPlayer: PIE session requested, will spawn MagePlayer on next tick");
    }

    // Delayed spawn: use a ticker to spawn MagePlayer after PIE initializes
    auto SpawnDelegate = [](float DeltaTime) -> bool
    {
        // Find the PIE world
        UWorld* PIEWorld = nullptr;
        for (const FWorldContext& Context : GEngine->GetWorldContexts())
        {
            if (Context.WorldType == EWorldType::PIE && Context.World())
            {
                PIEWorld = Context.World();
                break;
            }
        }
        if (!PIEWorld)
        {
            return false; // keep ticking until PIE world exists
        }

        // Find the player controller
        APlayerController* PC = nullptr;
        for (FConstPlayerControllerIterator It = PIEWorld->GetPlayerControllerIterator(); It; ++It)
        {
            PC = It->Get();
            break;
        }
        if (!PC)
        {
            return false; // keep ticking until PC exists
        }

        // Check if already spawned
        if (PC->GetPawn() && PC->GetPawn()->IsA(APawn::StaticClass()))
        {
            // Already has a pawn — check if it's already a MagePlayer
            if (PC->GetPawn()->GetName().Contains(TEXT("MagePlayer")))
            {
                return true; // done
            }
        }

        // Spawn MagePlayer
        UClass* PawnClass = LoadClass<APawn>(nullptr, TEXT("/Script/Lute.MagePlayer"));
        if (!PawnClass)
        {
            LUTE_LOG("SpawnPlayer: Failed to load MagePlayer class for delayed spawn");
            return true; // stop trying
        }

        // Get spawn location from PlayerStart
        FVector SpawnLoc(0, 0, 1000);
        FRotator SpawnRot(0, 0, 0);
        for (TActorIterator<APlayerStart> It(PIEWorld); It; ++It)
        {
            SpawnLoc = It->GetActorLocation();
            SpawnRot = It->GetActorRotation();
            break;
        }

        FActorSpawnParameters SpawnParams;
        SpawnParams.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AdjustIfPossibleButAlwaysSpawn;
        APawn* NewPawn = PIEWorld->SpawnActor<APawn>(PawnClass, SpawnLoc, SpawnRot, SpawnParams);
        if (NewPawn)
        {
            PC->Possess(NewPawn);
            LUTE_LOG("SpawnPlayer: Spawned MagePlayer at (%.1f, %.1f, %.1f) and possessed", SpawnLoc.X, SpawnLoc.Y, SpawnLoc.Z);
        }
        else
        {
            LUTE_LOG("SpawnPlayer: Failed to spawn MagePlayer");
        }
        return true; // stop ticking
    };

    FTSTicker::GetCoreTicker().AddTicker(FTickerDelegate::CreateLambda(SpawnDelegate), 0.1f);

    Json->SetBoolField("success", true);
    Json->SetStringField("pawn_class", "MagePlayer");
    Callback(MakeJsonResponse(JsonToString(Json)));
    return true;
}

void FLuteRemoteControlModule::StartupModule()
{
    LUTE_LOG("Starting Lute Remote Control server on port 6410...");

    FHttpServerModule& HttpServerModule = FHttpServerModule::Get();
    TSharedRef<IHttpRouter> Router = HttpServerModule.GetHttpRouter(6410).ToSharedRef();

    // GET /state
    Router->BindRoute(FHttpPath(TEXT("/state")), EHttpServerRequestVerbs::VERB_GET,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleGetState(Request, Callback);
        }));

    // POST /command
    Router->BindRoute(FHttpPath(TEXT("/command")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleCommand(Request, Callback);
        }));

    // POST /spawn
    Router->BindRoute(FHttpPath(TEXT("/spawn")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleSpawn(Request, Callback);
        }));

    // GET /screenshot
    Router->BindRoute(FHttpPath(TEXT("/screenshot")), EHttpServerRequestVerbs::VERB_GET,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleScreenshot(Request, Callback);
        }));

    // POST /set_mesh
    Router->BindRoute(FHttpPath(TEXT("/set_mesh")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleSetMesh(Request, Callback);
        }));

    // POST /list_meshes
    Router->BindRoute(FHttpPath(TEXT("/list_meshes")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleListMeshes(Request, Callback);
        }));

    // POST /setup_fina_materials
    Router->BindRoute(FHttpPath(TEXT("/setup_fina_materials")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleSetupFinaMaterials(Request, Callback);
        }));

    // POST /setup_materials
    Router->BindRoute(FHttpPath(TEXT("/setup_materials")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleSetupMaterials(Request, Callback);
        }));

    // POST /list_materials
    Router->BindRoute(FHttpPath(TEXT("/list_materials")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleListMaterials(Request, Callback);
        }));

    // POST /toggle_material
    Router->BindRoute(FHttpPath(TEXT("/toggle_material")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleToggleMaterial(Request, Callback);
        }));

    // POST /load_asset
    Router->BindRoute(FHttpPath(TEXT("/load_asset")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleLoadAsset(Request, Callback);
        }));

    // POST /add_mesh
    Router->BindRoute(FHttpPath(TEXT("/add_mesh")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleAddMesh(Request, Callback);
        }));

    // POST /toggle_mesh
    Router->BindRoute(FHttpPath(TEXT("/toggle_mesh")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleToggleMesh(Request, Callback);
        }));

    // POST /exec
    Router->BindRoute(FHttpPath(TEXT("/exec")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleExec(Request, Callback);
        }));

    // POST /place_prop
    Router->BindRoute(FHttpPath(TEXT("/place_prop")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandlePlaceProp(Request, Callback);
        }));

    // POST /list_props
    Router->BindRoute(FHttpPath(TEXT("/list_props")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleListProps(Request, Callback);
        }));

    // POST /world_build
    Router->BindRoute(FHttpPath(TEXT("/world_build")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleWorldBuild(Request, Callback);
        }));

    // POST /batch_place — HISM batch placement
    Router->BindRoute(FHttpPath(TEXT("/batch_place")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleBatchPlace(Request, Callback);
        }));

    // POST /spawn_water — create water plane
    Router->BindRoute(FHttpPath(TEXT("/spawn_water")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleSpawnWater(Request, Callback);
        }));

    // POST /clear_world
    Router->BindRoute(FHttpPath(TEXT("/clear_world")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleClearWorld(Request, Callback);
        }));

    // POST /setup_landscape_material
    Router->BindRoute(FHttpPath(TEXT("/setup_landscape_material")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleSetupLandscapeMaterial(Request, Callback);
        }));

    // POST /setup_landscape_layers
    Router->BindRoute(FHttpPath(TEXT("/setup_landscape_layers")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleSetupLandscapeLayers(Request, Callback);
        }));

    // POST /delete_actor
    Router->BindRoute(FHttpPath(TEXT("/delete_actor")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleDeleteActor(Request, Callback);
        }));

    // POST /move_actor
    Router->BindRoute(FHttpPath(TEXT("/move_actor")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleMoveActor(Request, Callback);
        }));

    // POST /duplicate_actor
    Router->BindRoute(FHttpPath(TEXT("/duplicate_actor")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleDuplicateActor(Request, Callback);
        }));

    // POST /terrain_sculpt
    Router->BindRoute(FHttpPath(TEXT("/terrain_sculpt")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleTerrainSculpt(Request, Callback);
        }));

    // POST /set_heightmap — bulk heightmap upload
    Router->BindRoute(FHttpPath(TEXT("/set_heightmap")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleSetHeightmap(Request, Callback);
        }));

    // GET /landscape_info — query landscape dimensions
    Router->BindRoute(FHttpPath(TEXT("/landscape_info")), EHttpServerRequestVerbs::VERB_GET,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleLandscapeInfo(Request, Callback);
        }));

    // GET /get_terrain_height
    Router->BindRoute(FHttpPath(TEXT("/get_terrain_height")), EHttpServerRequestVerbs::VERB_GET,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleGetTerrainHeight(Request, Callback);
        }));

    // POST /batch_terrain_height — query many points in one request
    Router->BindRoute(FHttpPath(TEXT("/batch_terrain_height")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleBatchTerrainHeight(Request, Callback);
        }));

    // POST /terrain_paint
    Router->BindRoute(FHttpPath(TEXT("/terrain_paint")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleTerrainPaint(Request, Callback);
        }));

    // POST /set_layer_weightmap — bulk layer weightmap upload
    Router->BindRoute(FHttpPath(TEXT("/set_layer_weightmap")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleSetLayerWeightmap(Request, Callback);
        }));

    // POST /flush_landscape — flush pending landscape edits after multiple layer uploads
    Router->BindRoute(FHttpPath(TEXT("/flush_landscape")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleFlushLandscape(Request, Callback);
        }));

    // POST /terrain/scatter — WP-partitioned scatter spawning
    Router->BindRoute(FHttpPath(TEXT("/terrain/scatter")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleTerrainScatter(Request, Callback);
        }));

    // POST /spawn_player — set MagePlayer as pawn and start PIE
    Router->BindRoute(FHttpPath(TEXT("/spawn_player")), EHttpServerRequestVerbs::VERB_POST,
        FHttpRequestHandler::CreateLambda([](const FHttpServerRequest& Request, const FResponseCallback& Callback)
        {
            return HandleSpawnPlayer(Request, Callback);
        }));

    HttpServerModule.StartAllListeners();
    bServerRunning = true;

    LUTE_LOG("Lute Remote Control server listening on http://localhost:6410");
}

void FLuteRemoteControlModule::ShutdownModule()
{
    if (bServerRunning)
    {
        LUTE_LOG("Shutting down Lute Remote Control server");
        bServerRunning = false;
    }
}

IMPLEMENT_MODULE(FLuteRemoteControlModule, LuteRemoteControl)
