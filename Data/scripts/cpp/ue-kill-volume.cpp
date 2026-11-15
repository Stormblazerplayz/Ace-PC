/*
  KILL VOLUME  (Unreal Engine 5 C++)
  An invisible box (like lava or a bottomless pit). When a Pawn (your player
  character, or an enemy) walks into it, the pawn is destroyed. Players respawn
  at the Player Start after a short delay.

  THIS FILE HOLDS TWO FILES: the header (.h) and the source (.cpp). Split them like this:
  1. In Unreal: Tools > New C++ Class > Actor > Next > name it KillVolume > Create Class.
  2. Replace everything in KillVolume.h with the "KillVolume.h" part below.
  3. Replace everything in KillVolume.cpp with the "KillVolume.cpp" part below.
  4. IMPORTANT: change YOURPROJECT_API to your project's name in CAPITALS + _API
     (example: project "SpaceGame" -> SPACEGAME_API).
  5. Compile, drag KillVolume into the level and scale the box to cover your lava/pit.
  Make sure your level has a Player Start, so players have somewhere to respawn.
*/

// ===== KillVolume.h =====
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "KillVolume.generated.h"  // must be the LAST include in the header

class UBoxComponent;
class UPrimitiveComponent;
class AController;

UCLASS()
class YOURPROJECT_API AKillVolume : public AActor  // replace YOURPROJECT_API (see step 4)
{
	GENERATED_BODY()

public:
	AKillVolume();

protected:
	virtual void BeginPlay() override;

	// The invisible box that does the killing
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Kill Volume")
	TObjectPtr<UBoxComponent> KillBox;

	// Should players come back to life at the Player Start?
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Kill Volume")
	bool bRespawnPlayers = true;

	// Seconds to wait before respawning
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Kill Volume", meta = (ClampMin = "0.0"))
	float RespawnDelay = 1.0f;

	// Also destroy things that are NOT pawns (like falling crates)?
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Kill Volume")
	bool bDestroyOtherActors = false;

	// Called when something starts touching the box.
	// The parameters MUST match OnComponentBeginOverlap exactly, and it MUST be a UFUNCTION.
	UFUNCTION()
	void OnOverlapBegin(UPrimitiveComponent* OverlappedComponent, AActor* OtherActor,
		UPrimitiveComponent* OtherComp, int32 OtherBodyIndex, bool bFromSweep,
		const FHitResult& SweepResult);

private:
	void RespawnPlayer(TWeakObjectPtr<AController> WeakController);
};

// ===== KillVolume.cpp =====
#include "KillVolume.h"
#include "Components/BoxComponent.h"
#include "Engine/World.h"
#include "GameFramework/Controller.h"
#include "GameFramework/GameModeBase.h"
#include "GameFramework/Pawn.h"
#include "TimerManager.h"

AKillVolume::AKillVolume()
{
	PrimaryActorTick.bCanEverTick = false;  // no Tick needed, we only react to overlaps

	KillBox = CreateDefaultSubobject<UBoxComponent>(TEXT("KillBox"));
	RootComponent = KillBox;
	KillBox->SetBoxExtent(FVector(200.f, 200.f, 50.f));  // half-size in cm (so 4m x 4m x 1m)
	KillBox->SetCollisionProfileName(TEXT("Trigger"));    // overlap everything, block nothing
	KillBox->SetGenerateOverlapEvents(true);
	KillBox->ShapeColor = FColor::Red;                    // red outline in the editor
}

void AKillVolume::BeginPlay()
{
	Super::BeginPlay();
	// Tell the box to call OnOverlapBegin when something enters it
	KillBox->OnComponentBeginOverlap.AddDynamic(this, &AKillVolume::OnOverlapBegin);
}

void AKillVolume::OnOverlapBegin(UPrimitiveComponent* OverlappedComponent, AActor* OtherActor,
	UPrimitiveComponent* OtherComp, int32 OtherBodyIndex, bool bFromSweep,
	const FHitResult& SweepResult)
{
	// Only the server decides who dies (this also works fine in single player)
	if (!HasAuthority() || OtherActor == nullptr || OtherActor == this)
	{
		return;
	}

	if (APawn* Pawn = Cast<APawn>(OtherActor))
	{
		AController* Controller = Pawn->GetController();
		const bool bIsPlayer = Controller != nullptr && Controller->IsPlayerController();

		Pawn->Destroy();  // bye bye!

		if (bRespawnPlayers && bIsPlayer)
		{
			TWeakObjectPtr<AController> WeakController(Controller);
			if (RespawnDelay <= 0.f)
			{
				RespawnPlayer(WeakController);
			}
			else
			{
				// Wait RespawnDelay seconds, then call RespawnPlayer
				FTimerHandle RespawnTimer;
				GetWorldTimerManager().SetTimer(RespawnTimer,
					FTimerDelegate::CreateUObject(this, &AKillVolume::RespawnPlayer, WeakController),
					RespawnDelay, false);
			}
		}
		return;
	}

	if (bDestroyOtherActors)
	{
		OtherActor->Destroy();
	}
}

void AKillVolume::RespawnPlayer(TWeakObjectPtr<AController> WeakController)
{
	AController* Controller = WeakController.Get();
	UWorld* World = GetWorld();
	if (Controller == nullptr || World == nullptr)
	{
		return;  // the player left the game
	}

	// The Game Mode knows how to spawn a new character at a Player Start
	if (AGameModeBase* GameMode = World->GetAuthGameMode())
	{
		GameMode->RestartPlayer(Controller);
	}
}
