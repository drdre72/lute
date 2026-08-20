#include "LLMAgent.h"
#include "HttpModule.h"
#include "Interfaces/IHttpRequest.h"
#include "Interfaces/IHttpResponse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonReader.h"
#include "Dom/JsonObject.h"
#include "Kismet/GameplayStatics.h"
#include "Engine/World.h"
#include "Misc/Base64.h"
#include "Modules/ModuleManager.h"
#include "HighResScreenshot.h"

ULLMAgent::ULLMAgent()
{
    ApiEndpoint = TEXT("https://api.mistral.ai/v1/chat/completions");
    ApiKey = TEXT("");
    Model = TEXT("mistral-large-latest");
    LocalServerPort = 6410;
    bEnableVision = false;
    PendingToolCount = 0;
    CompletedToolCount = 0;
}

FString ULLMAgent::GetToolDefinitions() const
{
    TArray<TSharedPtr<FJsonValue>> Tools;

    auto MakeTool = [](const FString& Name, const FString& Desc, const TSharedPtr<FJsonObject>& Params) {
        TSharedPtr<FJsonObject> Tool = MakeShared<FJsonObject>();
        Tool->SetStringField("type", "function");
        TSharedPtr<FJsonObject> Func = MakeShared<FJsonObject>();
        Func->SetStringField("name", Name);
        Func->SetStringField("description", Desc);
        Func->SetObjectField("parameters", Params);
        Tool->SetObjectField("function", Func);
        return MakeShared<FJsonValueObject>(Tool);
    };

    auto MakeStringParam = [](const FString& Desc) {
        TSharedPtr<FJsonObject> P = MakeShared<FJsonObject>();
        P->SetStringField("type", "string");
        P->SetStringField("description", Desc);
        return P;
    };
    auto MakeNumberParam = [](const FString& Desc) {
        TSharedPtr<FJsonObject> P = MakeShared<FJsonObject>();
        P->SetStringField("type", "number");
        P->SetStringField("description", Desc);
        return P;
    };
    auto MakeArrayParam = [](const FString& Desc) {
        TSharedPtr<FJsonObject> P = MakeShared<FJsonObject>();
        P->SetStringField("type", "array");
        P->SetStringField("description", Desc);
        return P;
    };

    // place_prop — single prop (for unique structures)
    {
        TSharedPtr<FJsonObject> Props = MakeShared<FJsonObject>();
        Props->SetStringField("type", "object");
        TSharedPtr<FJsonObject> PP = MakeShared<FJsonObject>();
        PP->SetObjectField("mesh_path", MakeStringParam("Static mesh asset path"));
        PP->SetObjectField("x", MakeNumberParam("World X"));
        PP->SetObjectField("y", MakeNumberParam("World Y"));
        PP->SetObjectField("z", MakeNumberParam("World Z (default 0)"));
        PP->SetObjectField("yaw", MakeNumberParam("Rotation degrees (default 0)"));
        PP->SetObjectField("scale", MakeNumberParam("Scale (default 1.0)"));
        Props->SetObjectField("properties", PP);
        Props->SetArrayField("required", {MakeShared<FJsonValueString>("mesh_path"), MakeShared<FJsonValueString>("x"), MakeShared<FJsonValueString>("y")});
        Tools.Add(MakeTool("place_prop", "Place a single static mesh prop at a world position (for unique structures)", Props));
    }

    // batch_place — HISM batch placement for thousands of identical meshes
    {
        TSharedPtr<FJsonObject> Props = MakeShared<FJsonObject>();
        Props->SetStringField("type", "object");
        TSharedPtr<FJsonObject> PP = MakeShared<FJsonObject>();
        PP->SetObjectField("mesh_path", MakeStringParam("Static mesh asset path to instance"));
        PP->SetObjectField("placements", MakeArrayParam("Array of {x, y, z, yaw, scale} objects for each instance"));
        Props->SetObjectField("properties", PP);
        Props->SetArrayField("required", {MakeShared<FJsonValueString>("mesh_path"), MakeShared<FJsonValueString>("placements")});
        Tools.Add(MakeTool("batch_place", "Batch-place thousands of identical meshes as HISM instances (trees, rocks, etc). Single draw call.", Props));
    }

    // list_props
    {
        TSharedPtr<FJsonObject> Props = MakeShared<FJsonObject>();
        Props->SetStringField("type", "object");
        TSharedPtr<FJsonObject> PP = MakeShared<FJsonObject>();
        PP->SetObjectField("filter", MakeStringParam("Name filter (e.g. 'tree', 'wall')"));
        PP->SetObjectField("max", MakeNumberParam("Max results (default 200)"));
        Props->SetObjectField("properties", PP);
        Props->SetArrayField("required", {});
        Tools.Add(MakeTool("list_props", "List available static mesh assets with optional filter", Props));
    }

    // clear_world
    {
        TSharedPtr<FJsonObject> Props = MakeShared<FJsonObject>();
        Props->SetStringField("type", "object");
        Props->SetObjectField("properties", MakeShared<FJsonObject>());
        Tools.Add(MakeTool("clear_world", "Remove all placed world builder props from the level", Props));
    }

    // terrain_sculpt
    {
        TSharedPtr<FJsonObject> Props = MakeShared<FJsonObject>();
        Props->SetStringField("type", "object");
        TSharedPtr<FJsonObject> PP = MakeShared<FJsonObject>();
        PP->SetObjectField("x", MakeNumberParam("World X"));
        PP->SetObjectField("y", MakeNumberParam("World Y"));
        PP->SetObjectField("radius", MakeNumberParam("Brush radius"));
        PP->SetObjectField("strength", MakeNumberParam("Brush strength"));
        PP->SetObjectField("mode", MakeStringParam("raise, lower, flatten, smooth"));
        Props->SetObjectField("properties", PP);
        Props->SetArrayField("required", {MakeShared<FJsonValueString>("x"), MakeShared<FJsonValueString>("y")});
        Tools.Add(MakeTool("terrain_sculpt", "Sculpt terrain height at a position", Props));
    }

    // terrain_paint
    {
        TSharedPtr<FJsonObject> Props = MakeShared<FJsonObject>();
        Props->SetStringField("type", "object");
        TSharedPtr<FJsonObject> PP = MakeShared<FJsonObject>();
        PP->SetObjectField("x", MakeNumberParam("World X"));
        PP->SetObjectField("y", MakeNumberParam("World Y"));
        PP->SetObjectField("radius", MakeNumberParam("Brush radius"));
        PP->SetObjectField("strength", MakeNumberParam("Paint strength 0-1"));
        PP->SetObjectField("layer", MakeStringParam("Layer: Grass, Dirt, Rock, Sand"));
        Props->SetObjectField("properties", PP);
        Props->SetArrayField("required", {});
        Tools.Add(MakeTool("terrain_paint", "Paint a terrain layer at a position", Props));
    }

    // get_terrain_height
    {
        TSharedPtr<FJsonObject> Props = MakeShared<FJsonObject>();
        Props->SetStringField("type", "object");
        TSharedPtr<FJsonObject> PP = MakeShared<FJsonObject>();
        PP->SetObjectField("x", MakeNumberParam("World X"));
        PP->SetObjectField("y", MakeNumberParam("World Y"));
        Props->SetObjectField("properties", PP);
        Props->SetArrayField("required", {});
        Tools.Add(MakeTool("get_terrain_height", "Query terrain height at a world position", Props));
    }

    // move_actor
    {
        TSharedPtr<FJsonObject> Props = MakeShared<FJsonObject>();
        Props->SetStringField("type", "object");
        TSharedPtr<FJsonObject> PP = MakeShared<FJsonObject>();
        PP->SetObjectField("actor_name", MakeStringParam("Actor name"));
        PP->SetObjectField("x", MakeNumberParam("New X"));
        PP->SetObjectField("y", MakeNumberParam("New Y"));
        PP->SetObjectField("z", MakeNumberParam("New Z"));
        PP->SetObjectField("yaw", MakeNumberParam("New yaw"));
        Props->SetObjectField("properties", PP);
        Props->SetArrayField("required", {MakeShared<FJsonValueString>("actor_name")});
        Tools.Add(MakeTool("move_actor", "Move an existing actor", Props));
    }

    // delete_actor
    {
        TSharedPtr<FJsonObject> Props = MakeShared<FJsonObject>();
        Props->SetStringField("type", "object");
        TSharedPtr<FJsonObject> PP = MakeShared<FJsonObject>();
        PP->SetObjectField("actor_name", MakeStringParam("Actor name to delete"));
        Props->SetObjectField("properties", PP);
        Props->SetArrayField("required", {MakeShared<FJsonValueString>("actor_name")});
        Tools.Add(MakeTool("delete_actor", "Delete an actor by name", Props));
    }

    // screenshot
    {
        TSharedPtr<FJsonObject> Props = MakeShared<FJsonObject>();
        Props->SetStringField("type", "object");
        Props->SetObjectField("properties", MakeShared<FJsonObject>());
        Tools.Add(MakeTool("screenshot", "Capture a screenshot of the current viewport", Props));
    }

    FString OutStr;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
    FJsonSerializer::Serialize(Tools, Writer);
    return OutStr;
}

FString ULLMAgent::CaptureScreenshotBase64()
{
    // Use the high-res screenshot system to capture the viewport
    FHighResScreenshotConfig& Config = GetHighResScreenshotConfig();
    Config.SetResolution(1024, 1024);
    Config.SetMaskEnabled(false);

    FString ScreenshotPath = FPaths::ProjectSavedDir() / TEXT("LLMAgent_Screenshot.jpg");

    // Capture via console command
    UWorld* World = GetWorld();
    if (!World) return TEXT("");

    APlayerController* PC = World->GetFirstPlayerController();
    if (!PC) return TEXT("");

    FString Cmd = FString::Printf(TEXT("shot %s"), *ScreenshotPath);
    PC->ConsoleCommand(Cmd, true);

    // Wait one frame for capture to complete is not possible in async context
    // Read the file if it exists from a previous capture
    TArray<uint8> FileData;
    if (FFileHelper::LoadFileToArray(FileData, *ScreenshotPath))
    {
        FString Base64 = FBase64::Encode(FileData);
        return FString::Printf(TEXT("data:image/jpeg;base64,%s"), *Base64);
    }

    return TEXT("");
}

TArray<TSharedPtr<FJsonValue>> ULLMAgent::BuildMultimodalContent(const FString& Text, const FString& Base64Image)
{
    TArray<TSharedPtr<FJsonValue>> Content;

    // Text part
    TSharedPtr<FJsonObject> TextPart = MakeShared<FJsonObject>();
    TextPart->SetStringField("type", "text");
    TextPart->SetStringField("text", Text);
    Content.Add(MakeShared<FJsonValueObject>(TextPart));

    // Image part
    if (!Base64Image.IsEmpty())
    {
        TSharedPtr<FJsonObject> ImagePart = MakeShared<FJsonObject>();
        ImagePart->SetStringField("type", "image_url");
        TSharedPtr<FJsonObject> ImageUrl = MakeShared<FJsonObject>();
        ImageUrl->SetStringField("url", Base64Image);
        ImagePart->SetObjectField("image_url", ImageUrl);
        Content.Add(MakeShared<FJsonValueObject>(ImagePart));
    }

    return Content;
}

void ULLMAgent::SendCommand(const FString& Command)
{
    UE_LOG(LogTemp, Log, TEXT("[LLMAgent] Command: %s"), *Command);
    LastCommand = Command;

    if (ApiKey.IsEmpty())
    {
        UE_LOG(LogTemp, Error, TEXT("[LLMAgent] No API key set! Set ApiKey property."));
        OnComplete.Broadcast(false);
        return;
    }

    FString ToolDefs = GetToolDefinitions();

    // Capture screenshot if vision is enabled
    FString ScreenshotBase64;
    if (bEnableVision)
    {
        ScreenshotBase64 = CaptureScreenshotBase64();
        UE_LOG(LogTemp, Log, TEXT("[LLMAgent] Vision enabled, screenshot: %d chars"), ScreenshotBase64.Len());
    }

    // Build system prompt
    FString SystemPrompt = FString::Printf(
        TEXT("You are a world builder agent for Unreal Engine. You control a medieval world via tool calls. ")
        TEXT("The map is 3km x 3km (coordinates -1500 to 1500). Build coherent structures with intentional placement. ")
        TEXT("Available tools: %s\n")
        TEXT("Always use tool calls to place props, sculpt terrain, and manage the world. ")
        TEXT("Use batch_place for trees, rocks, and repeated props (thousands at once). ")
        TEXT("Use place_prop for unique structures (buildings, monuments). ")
        TEXT("Group structures logically like a Rust map: ring roads connecting monuments, forests in natural zones."),
        *ToolDefs);

    // Build messages array
    TSharedPtr<FJsonObject> RequestObj = MakeShared<FJsonObject>();
    RequestObj->SetStringField("model", Model);

    TArray<TSharedPtr<FJsonValue>> Messages;

    // System message
    {
        TSharedPtr<FJsonObject> SysMsg = MakeShared<FJsonObject>();
        SysMsg->SetStringField("role", "system");
        SysMsg->SetStringField("content", SystemPrompt);
        Messages.Add(MakeShared<FJsonValueObject>(SysMsg));
    }

    // User message — multimodal if vision enabled
    {
        TSharedPtr<FJsonObject> UserMsg = MakeShared<FJsonObject>();
        UserMsg->SetStringField("role", "user");

        if (bEnableVision && !ScreenshotBase64.IsEmpty())
        {
            // Multimodal content array
            UserMsg->SetArrayField("content", BuildMultimodalContent(Command, ScreenshotBase64));
        }
        else
        {
            UserMsg->SetStringField("content", Command);
        }
        Messages.Add(MakeShared<FJsonValueObject>(UserMsg));
    }

    RequestObj->SetArrayField("messages", Messages);

    // Parse tool definitions
    TArray<TSharedPtr<FJsonValue>> Tools;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(ToolDefs);
    FJsonSerializer::Deserialize(Reader, Tools);
    RequestObj->SetArrayField("tools", Tools);

    // Serialize request
    FString RequestBody;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&RequestBody);
    FJsonSerializer::Serialize(RequestObj.ToSharedRef(), Writer);

    UE_LOG(LogTemp, Log, TEXT("[LLMAgent] Sending async request to %s"), *ApiEndpoint);

    // Make async HTTP request to LLM API — NO blocking
    TSharedRef<IHttpRequest, ESPMode::ThreadSafe> HttpRequest = FHttpModule::Get().CreateRequest();
    HttpRequest->SetURL(ApiEndpoint);
    HttpRequest->SetVerb("POST");
    HttpRequest->SetHeader("Content-Type", "application/json");
    HttpRequest->SetHeader("Authorization", FString::Printf(TEXT("Bearer %s"), *ApiKey));
    HttpRequest->SetContentAsString(RequestBody);
    HttpRequest->SetTimeout(120.0f);

    TWeakObjectPtr<ULLMAgent> WeakSelf(this);
    HttpRequest->OnProcessRequestComplete().BindLambda(
        [WeakSelf](FHttpRequestPtr Req, FHttpResponsePtr Resp, bool bSuccess) {
            if (ULLMAgent* Self = WeakSelf.Get())
            {
                Self->HandleLLMResponse(Req, Resp, bSuccess);
            }
        });

    HttpRequest->ProcessRequest();
}

void ULLMAgent::HandleLLMResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bSuccess)
{
    if (!bSuccess || !Response.IsValid())
    {
        UE_LOG(LogTemp, Error, TEXT("[LLMAgent] LLM HTTP request failed"));
        OnComplete.Broadcast(false);
        return;
    }

    FString ResponseStr = Response->GetContentAsString();
    UE_LOG(LogTemp, Log, TEXT("[LLMAgent] Response received (%d bytes)"), ResponseStr.Len());
    ProcessResponse(ResponseStr);
}

void ULLMAgent::ProcessResponse(const FString& Response)
{
    TSharedPtr<FJsonObject> ResponseObj;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Response);
    if (!FJsonSerializer::Deserialize(Reader, ResponseObj) || !ResponseObj.IsValid())
    {
        UE_LOG(LogTemp, Error, TEXT("[LLMAgent] Failed to parse LLM response"));
        UE_LOG(LogTemp, Error, TEXT("[LLMAgent] Response: %s"), *Response.Left(500));
        OnComplete.Broadcast(false);
        return;
    }

    if (ResponseObj->HasField(TEXT("error")))
    {
        TSharedPtr<FJsonObject> ErrorObj = ResponseObj->GetObjectField(TEXT("error"));
        FString ErrorMsg = ErrorObj.IsValid() ? ErrorObj->GetStringField(TEXT("message")) : TEXT("unknown");
        UE_LOG(LogTemp, Error, TEXT("[LLMAgent] API error: %s"), *ErrorMsg);
        OnComplete.Broadcast(false);
        return;
    }

    const TArray<TSharedPtr<FJsonValue>>* Choices;
    if (ResponseObj->TryGetArrayField(TEXT("choices"), Choices) && Choices->Num() > 0)
    {
        TSharedPtr<FJsonObject> Choice = (*Choices)[0]->AsObject();
        if (Choice.IsValid())
        {
            TSharedPtr<FJsonObject> Message = Choice->GetObjectField(TEXT("message"));
            if (Message.IsValid())
            {
                FString Content = Message->GetStringField(TEXT("content"));
                if (!Content.IsEmpty())
                {
                    UE_LOG(LogTemp, Log, TEXT("[LLMAgent] Content: %s"), *Content);
                }

                ParseToolCalls(Message);
            }
        }
    }
}

void ULLMAgent::ParseToolCalls(const TSharedPtr<FJsonObject>& ResponseObj)
{
    const TArray<TSharedPtr<FJsonValue>>* ToolCalls;
    if (!ResponseObj->TryGetArrayField(TEXT("tool_calls"), ToolCalls))
    {
        UE_LOG(LogTemp, Log, TEXT("[LLMAgent] No tool calls in response — done"));
        OnComplete.Broadcast(true);
        return;
    }

    PendingToolCount = ToolCalls->Num();
    CompletedToolCount = 0;
    UE_LOG(LogTemp, Log, TEXT("[LLMAgent] %d tool calls to execute"), PendingToolCount);

    int32 Index = 0;
    for (const TSharedPtr<FJsonValue>& ToolCallVal : *ToolCalls)
    {
        TSharedPtr<FJsonObject> ToolCall = ToolCallVal->AsObject();
        if (!ToolCall.IsValid()) { CompletedToolCount++; continue; }

        TSharedPtr<FJsonObject> Function = ToolCall->GetObjectField(TEXT("function"));
        if (!Function.IsValid()) { CompletedToolCount++; continue; }

        FString ToolName = Function->GetStringField(TEXT("name"));
        FString ArgsStr = Function->GetStringField(TEXT("arguments"));

        UE_LOG(LogTemp, Log, TEXT("[LLMAgent] Tool call %d/%d: %s"), Index + 1, PendingToolCount, *ToolName);

        TSharedPtr<FJsonObject> Args;
        TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(ArgsStr);
        FJsonSerializer::Deserialize(Reader, Args);

        if (!Args.IsValid())
        {
            UE_LOG(LogTemp, Warning, TEXT("[LLMAgent] Failed to parse args: %s"), *ArgsStr);
            CompletedToolCount++;
            continue;
        }

        // Execute async — no blocking
        ExecuteToolCallAsync(ToolName, Args, Index, PendingToolCount);
        Index++;
    }

    // If all tools were invalid, complete now
    if (CompletedToolCount >= PendingToolCount)
    {
        OnComplete.Broadcast(true);
    }
}

void ULLMAgent::ExecuteToolCallAsync(const FString& ToolName, const TSharedPtr<FJsonObject>& Args, int32 ToolIndex, int32 TotalTools)
{
    UE_LOG(LogTemp, Log, TEXT("[LLMAgent] Executing tool async: %s (%d/%d)"), *ToolName, ToolIndex + 1, TotalTools);

    // Map tool names to local HTTP endpoints
    FString Endpoint;
    FString Body;
    bool bIsGet = false;
    FString GetUrl;

    if (ToolName == "place_prop")
    {
        Endpoint = TEXT("/place_prop");
        TSharedPtr<FJsonObject> BodyObj = MakeShared<FJsonObject>();
        BodyObj->SetStringField("mesh_path", Args->GetStringField(TEXT("mesh_path")));
        BodyObj->SetNumberField("x", Args->GetNumberField(TEXT("x")));
        BodyObj->SetNumberField("y", Args->GetNumberField(TEXT("y")));
        BodyObj->SetNumberField("z", Args->HasField(TEXT("z")) ? Args->GetNumberField(TEXT("z")) : 0.0);
        BodyObj->SetNumberField("yaw", Args->HasField(TEXT("yaw")) ? Args->GetNumberField(TEXT("yaw")) : 0.0);
        BodyObj->SetNumberField("scale", Args->HasField(TEXT("scale")) ? Args->GetNumberField(TEXT("scale")) : 1.0);
        FString OutStr;
        TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
        FJsonSerializer::Serialize(BodyObj.ToSharedRef(), Writer);
        Body = OutStr;
        double X = Args->GetNumberField(TEXT("x"));
        double Y = Args->GetNumberField(TEXT("y"));
        double Z = Args->HasField(TEXT("z")) ? Args->GetNumberField(TEXT("z")) : 0.0;
        OnToolCall.Broadcast("place_prop", FVector(X, Y, Z));
    }
    else if (ToolName == "batch_place")
    {
        Endpoint = TEXT("/batch_place");
        TSharedPtr<FJsonObject> BodyObj = MakeShared<FJsonObject>();
        BodyObj->SetStringField("mesh_path", Args->GetStringField(TEXT("mesh_path")));
        // Pass placements array through
        const TArray<TSharedPtr<FJsonValue>>* Placements = nullptr;
        if (Args->TryGetArrayField(TEXT("placements"), Placements))
        {
            BodyObj->SetArrayField("placements", *Placements);
        }
        FString OutStr;
        TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
        FJsonSerializer::Serialize(BodyObj.ToSharedRef(), Writer);
        Body = OutStr;
        OnToolCall.Broadcast("batch_place", FVector::ZeroVector);
    }
    else if (ToolName == "list_props")
    {
        Endpoint = TEXT("/list_props");
        TSharedPtr<FJsonObject> BodyObj = MakeShared<FJsonObject>();
        BodyObj->SetStringField("filter", Args->HasField(TEXT("filter")) ? Args->GetStringField(TEXT("filter")) : TEXT(""));
        BodyObj->SetNumberField("max", Args->HasField(TEXT("max")) ? Args->GetNumberField(TEXT("max")) : 200);
        FString OutStr;
        TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
        FJsonSerializer::Serialize(BodyObj.ToSharedRef(), Writer);
        Body = OutStr;
    }
    else if (ToolName == "clear_world")
    {
        Endpoint = TEXT("/clear_world");
        Body = TEXT("{}");
        OnToolCall.Broadcast("clear_world", FVector::ZeroVector);
    }
    else if (ToolName == "terrain_sculpt")
    {
        Endpoint = TEXT("/terrain_sculpt");
        TSharedPtr<FJsonObject> BodyObj = MakeShared<FJsonObject>();
        BodyObj->SetNumberField("x", Args->GetNumberField(TEXT("x")));
        BodyObj->SetNumberField("y", Args->GetNumberField(TEXT("y")));
        BodyObj->SetNumberField("radius", Args->HasField(TEXT("radius")) ? Args->GetNumberField(TEXT("radius")) : 500.0);
        BodyObj->SetNumberField("strength", Args->HasField(TEXT("strength")) ? Args->GetNumberField(TEXT("strength")) : 100.0);
        BodyObj->SetStringField("mode", Args->HasField(TEXT("mode")) ? Args->GetStringField(TEXT("mode")) : TEXT("raise"));
        FString OutStr;
        TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
        FJsonSerializer::Serialize(BodyObj.ToSharedRef(), Writer);
        Body = OutStr;
        OnToolCall.Broadcast("terrain_sculpt", FVector(Args->GetNumberField(TEXT("x")), Args->GetNumberField(TEXT("y")), 0));
    }
    else if (ToolName == "terrain_paint")
    {
        Endpoint = TEXT("/terrain_paint");
        TSharedPtr<FJsonObject> BodyObj = MakeShared<FJsonObject>();
        BodyObj->SetNumberField("x", Args->GetNumberField(TEXT("x")));
        BodyObj->SetNumberField("y", Args->GetNumberField(TEXT("y")));
        BodyObj->SetNumberField("radius", Args->HasField(TEXT("radius")) ? Args->GetNumberField(TEXT("radius")) : 500.0);
        BodyObj->SetNumberField("strength", Args->HasField(TEXT("strength")) ? Args->GetNumberField(TEXT("strength")) : 0.5);
        BodyObj->SetStringField("layer", Args->HasField(TEXT("layer")) ? Args->GetStringField(TEXT("layer")) : TEXT("Grass"));
        FString OutStr;
        TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
        FJsonSerializer::Serialize(BodyObj.ToSharedRef(), Writer);
        Body = OutStr;
    }
    else if (ToolName == "get_terrain_height")
    {
        double X = Args->GetNumberField(TEXT("x"));
        double Y = Args->GetNumberField(TEXT("y"));
        GetUrl = FString::Printf(TEXT("/get_terrain_height?x=%.1f&y=%.1f"), X, Y);
        bIsGet = true;
    }
    else if (ToolName == "move_actor")
    {
        Endpoint = TEXT("/move_actor");
        TSharedPtr<FJsonObject> BodyObj = MakeShared<FJsonObject>();
        BodyObj->SetStringField("actor_name", Args->GetStringField(TEXT("actor_name")));
        BodyObj->SetNumberField("x", Args->GetNumberField(TEXT("x")));
        BodyObj->SetNumberField("y", Args->GetNumberField(TEXT("y")));
        BodyObj->SetNumberField("z", Args->HasField(TEXT("z")) ? Args->GetNumberField(TEXT("z")) : 0.0);
        BodyObj->SetNumberField("yaw", Args->HasField(TEXT("yaw")) ? Args->GetNumberField(TEXT("yaw")) : 0.0);
        FString OutStr;
        TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
        FJsonSerializer::Serialize(BodyObj.ToSharedRef(), Writer);
        Body = OutStr;
    }
    else if (ToolName == "delete_actor")
    {
        Endpoint = TEXT("/delete_actor");
        TSharedPtr<FJsonObject> BodyObj = MakeShared<FJsonObject>();
        BodyObj->SetStringField("actor_name", Args->GetStringField(TEXT("actor_name")));
        FString OutStr;
        TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
        FJsonSerializer::Serialize(BodyObj.ToSharedRef(), Writer);
        Body = OutStr;
    }
    else if (ToolName == "screenshot")
    {
        GetUrl = TEXT("/screenshot");
        bIsGet = true;
    }
    else
    {
        UE_LOG(LogTemp, Warning, TEXT("[LLMAgent] Unknown tool: %s"), *ToolName);
        CompletedToolCount++;
        if (CompletedToolCount >= PendingToolCount)
        {
            OnComplete.Broadcast(true);
        }
        return;
    }

    // Make async HTTP request to local server — NO blocking
    FString Url;
    if (bIsGet)
    {
        Url = FString::Printf(TEXT("http://localhost:%d%s"), LocalServerPort, *GetUrl);
    }
    else
    {
        Url = FString::Printf(TEXT("http://localhost:%d%s"), LocalServerPort, *Endpoint);
    }

    TSharedRef<IHttpRequest, ESPMode::ThreadSafe> LocalReq = FHttpModule::Get().CreateRequest();
    LocalReq->SetURL(Url);
    LocalReq->SetTimeout(60.0f);

    if (bIsGet)
    {
        LocalReq->SetVerb("GET");
    }
    else
    {
        LocalReq->SetVerb("POST");
        LocalReq->SetHeader("Content-Type", "application/json");
        LocalReq->SetContentAsString(Body);
    }

    TWeakObjectPtr<ULLMAgent> WeakSelf(this);
    LocalReq->OnProcessRequestComplete().BindLambda(
        [WeakSelf, ToolName, ToolIndex, TotalTools](FHttpRequestPtr Req, FHttpResponsePtr Resp, bool bSuccess) {
            if (ULLMAgent* Self = WeakSelf.Get())
            {
                Self->HandleToolResponse(Req, Resp, bSuccess, ToolName, ToolIndex, TotalTools);
            }
        });

    LocalReq->ProcessRequest();
}

void ULLMAgent::HandleToolResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bSuccess, FString ToolName, int32 ToolIndex, int32 TotalTools)
{
    FString ResultStr;
    if (bSuccess && Response.IsValid())
    {
        ResultStr = Response->GetContentAsString();
    }

    UE_LOG(LogTemp, Log, TEXT("[LLMAgent] Tool '%s' (%d/%d) complete: %s"),
        *ToolName, ToolIndex + 1, TotalTools, *ResultStr.Left(200));

    CompletedToolCount++;

    if (CompletedToolCount >= PendingToolCount)
    {
        UE_LOG(LogTemp, Log, TEXT("[LLMAgent] All %d tool calls complete"), PendingToolCount);
        OnComplete.Broadcast(true);
    }
}

void ULLMAgent::SendFollowUp()
{
    // Could implement multi-turn conversation here by sending tool results back to LLM
    // For now, just log
    UE_LOG(LogTemp, Log, TEXT("[LLMAgent] Follow-up turn (not yet implemented)"));
}
