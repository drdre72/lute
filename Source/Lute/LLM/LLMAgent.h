#pragma once

#include "CoreMinimal.h"
#include "UObject/NoExportTypes.h"
#include "HttpFwd.h"
#include "LLMAgent.generated.h"

struct FInventoryItem;

UCLASS(BlueprintType)
class LUTE_API ULLMAgent : public UObject
{
    GENERATED_BODY()

public:
    ULLMAgent();

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LLM")
    FString ApiEndpoint;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LLM")
    FString ApiKey;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LLM")
    FString Model;

    // Local Unreal HTTP server port (default 6410)
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LLM")
    int32 LocalServerPort;

    // Whether to capture and send screenshots with each LLM turn (vision mode)
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "LLM")
    bool bEnableVision;

    // Send a building command to the LLM and execute the response (async)
    UFUNCTION(BlueprintCallable, Category = "Lute|LLM")
    void SendCommand(const FString& Command);

    // Get available tools as JSON string for LLM system prompt
    FString GetToolDefinitions() const;

    // Execute a tool call by dispatching to local HTTP server (async)
    void ExecuteToolCallAsync(const FString& ToolName, const TSharedPtr<FJsonObject>& Args, int32 ToolIndex, int32 TotalTools);

    // Process LLM response and execute tool calls
    void ProcessResponse(const FString& Response);

    DECLARE_DYNAMIC_MULTICAST_DELEGATE_TwoParams(FOnToolCall, FString, Action, FVector, TargetPosition);
    DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnComplete, bool, bSuccess);

    UPROPERTY(BlueprintAssignable, Category = "Lute|LLM")
    FOnToolCall OnToolCall;

    UPROPERTY(BlueprintAssignable, Category = "Lute|LLM")
    FOnComplete OnComplete;

private:
    // Handle completed HTTP response from LLM API
    void HandleLLMResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bSuccess);

    // Handle completed local server response for a tool call
    void HandleToolResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bSuccess, FString ToolName, int32 ToolIndex, int32 TotalTools);

    // Capture screenshot and return as Base64 JPEG string
    FString CaptureScreenshotBase64();

    // Build multimodal message content (text + image) for vision LLM
    TArray<TSharedPtr<FJsonValue>> BuildMultimodalContent(const FString& Text, const FString& Base64Image);

    // Parse and execute tool calls from LLM JSON response
    void ParseToolCalls(const TSharedPtr<FJsonObject>& ResponseObj);

    // Track async tool execution
    int32 PendingToolCount;
    int32 CompletedToolCount;
    FString LastCommand;

    // Send follow-up LLM request after tool execution completes
    void SendFollowUp();
};
