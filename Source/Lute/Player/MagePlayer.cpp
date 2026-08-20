#include "MagePlayer.h"
#include "../Spells/SpellComponent.h"
#include "../Inventory/InventoryComponent.h"
#include "GameFramework/SpringArmComponent.h"
#include "Camera/CameraComponent.h"
#include "Components/CapsuleComponent.h"
#include "Components/StaticMeshComponent.h"
#include "UObject/ConstructorHelpers.h"
#include "Kismet/GameplayStatics.h"
#include "GameFramework/CharacterMovementComponent.h"

AMagePlayer::AMagePlayer()
{
    PrimaryActorTick.bCanEverTick = true;

    // Visible body mesh (simple capsule shape for testing)
    BodyMesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("BodyMesh"));
    BodyMesh->SetupAttachment(RootComponent);
    static ConstructorHelpers::FObjectFinder<UStaticMesh> CapsuleMesh(TEXT("/Engine/BasicShapes/Cylinder.Cylinder"));
    if (CapsuleMesh.Succeeded())
    {
        BodyMesh->SetStaticMesh(CapsuleMesh.Object);
    }
    BodyMesh->SetRelativeScale3D(FVector(0.5f, 0.5f, 1.0f));
    BodyMesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);

    CameraBoom = CreateDefaultSubobject<USpringArmComponent>(TEXT("CameraBoom"));
    CameraBoom->SetupAttachment(RootComponent);
    CameraBoom->TargetArmLength = 600.0f;
    CameraBoom->bUsePawnControlRotation = true;

    Camera = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
    Camera->SetupAttachment(CameraBoom, USpringArmComponent::SocketName);
    Camera->bUsePawnControlRotation = false;

    SpellComp = CreateDefaultSubobject<USpellComponent>(TEXT("SpellComponent"));
    InventoryComp = CreateDefaultSubobject<UInventoryComponent>(TEXT("InventoryComponent"));

    CastRange = 2000.0f;

    // Movement setup
    bUseControllerRotationPitch = false;
    bUseControllerRotationYaw = false;
    bUseControllerRotationRoll = false;

    if (UCharacterMovementComponent* MoveComp = GetCharacterMovement())
    {
        MoveComp->bOrientRotationToMovement = true;
        MoveComp->RotationRate = FRotator(0.0f, 500.0f, 0.0f);
        MoveComp->MaxWalkSpeed = 600.0f;
        MoveComp->AirControl = 0.2f;
    }
}

void AMagePlayer::BeginPlay()
{
    Super::BeginPlay();
    UE_LOG(LogTemp, Log, TEXT("[MagePlayer] Spawned"));
}

void AMagePlayer::SetupPlayerInputComponent(UInputComponent* PlayerInputComponent)
{
    Super::SetupPlayerInputComponent(PlayerInputComponent);

    // Movement
    PlayerInputComponent->BindAxis("MoveForward", this, &AMagePlayer::MoveForward);
    PlayerInputComponent->BindAxis("MoveRight", this, &AMagePlayer::MoveRight);
    PlayerInputComponent->BindAxis("Turn", this, &APawn::AddControllerYawInput);
    PlayerInputComponent->BindAxis("LookUp", this, &APawn::AddControllerPitchInput);

    // Actions
    PlayerInputComponent->BindAction("Sprint", IE_Pressed, this, &AMagePlayer::StartSprint);
    PlayerInputComponent->BindAction("Sprint", IE_Released, this, &AMagePlayer::StopSprint);
    PlayerInputComponent->BindAction("Jump", IE_Pressed, this, &ACharacter::Jump);
    PlayerInputComponent->BindAction("Jump", IE_Released, this, &ACharacter::StopJumping);

    PlayerInputComponent->BindAction("CastSpell", IE_Pressed, this, &AMagePlayer::CastAtCrosshair);
    PlayerInputComponent->BindAction("SwapTool", IE_Pressed, this, &AMagePlayer::SwapTool);

    // Number keys for clothing removal — use lambda to pass slot index
    PlayerInputComponent->BindAction("RemoveClothing1", IE_Pressed, this, &AMagePlayer::RemoveClothingHead);
    PlayerInputComponent->BindAction("RemoveClothing2", IE_Pressed, this, &AMagePlayer::RemoveClothingChest);
    PlayerInputComponent->BindAction("RemoveClothing3", IE_Pressed, this, &AMagePlayer::RemoveClothingLegs);
    PlayerInputComponent->BindAction("RemoveClothing4", IE_Pressed, this, &AMagePlayer::RemoveClothingFeet);
    PlayerInputComponent->BindAction("RemoveClothing5", IE_Pressed, this, &AMagePlayer::RemoveClothingHands);
}

void AMagePlayer::MoveForward(float Value)
{
    if (Controller && Value != 0.0f)
    {
        const FRotator Rotation = Controller->GetControlRotation();
        const FRotator YawRotation(0, Rotation.Yaw, 0);
        const FVector Direction = FRotationMatrix(YawRotation).GetUnitAxis(EAxis::X);
        AddMovementInput(Direction, Value);
    }
}

void AMagePlayer::MoveRight(float Value)
{
    if (Controller && Value != 0.0f)
    {
        const FRotator Rotation = Controller->GetControlRotation();
        const FRotator YawRotation(0, Rotation.Yaw, 0);
        const FVector Direction = FRotationMatrix(YawRotation).GetUnitAxis(EAxis::Y);
        AddMovementInput(Direction, Value);
    }
}

void AMagePlayer::StartSprint()
{
    if (UCharacterMovementComponent* MoveComp = GetCharacterMovement())
    {
        MoveComp->MaxWalkSpeed = 1200.0f;
    }
}

void AMagePlayer::StopSprint()
{
    if (UCharacterMovementComponent* MoveComp = GetCharacterMovement())
    {
        MoveComp->MaxWalkSpeed = 600.0f;
    }
}

FVector AMagePlayer::GetLookTarget() const
{
    FVector Start = Camera->GetComponentLocation();
    FVector Dir = Camera->GetForwardVector();
    return Start + Dir * CastRange;
}

void AMagePlayer::CastAtCrosshair()
{
    if (!SpellComp || SpellComp->bIsCasting) return;
    FVector Target = GetLookTarget();
    SpellComp->CastSpell(Target);
}

void AMagePlayer::SwapTool()
{
    if (InventoryComp)
    {
        InventoryComp->SwapTool();
    }
}

void AMagePlayer::RemoveClothing(int32 SlotIndex)
{
    if (InventoryComp)
    {
        InventoryComp->RemoveClothing(SlotIndex);
    }
}
