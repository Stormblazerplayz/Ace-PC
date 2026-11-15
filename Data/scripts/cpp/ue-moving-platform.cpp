/*
  MOVING PLATFORM  (Unreal Engine 5 C++)
  A platform that moves back and forth (ping-pong) between where it starts and a
  target spot, pausing at each end. Players standing on it ride along.

  THIS FILE HOLDS TWO FILES: the header (.h) and the source (.cpp). Split them like this:
  1. In Unreal: Tools > New C++ Class > Actor > Next > name it MovingPlatform > Create Class.
  2. Replace everything in MovingPlatform.h with the "MovingPlatform.h" part below.
  3. Replace everything in MovingPlatform.cpp with the "MovingPlatform.cpp" part below.
  4. IMPORTANT: change YOURPROJECT_API to your project's name in CAPITALS + _API
     (example: project "SpaceGame" -> SPACEGAME_API).
  5. Compile, drag MovingPlatform into the level and give it a Static Mesh (a cube works).
  6. Select it: you'll see a little diamond called "Target Offset". Drag the diamond to
     where the platform should travel to!
*/

// ===== MovingPlatform.h =====
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "MovingPlatform.generated.h"  // must be the LAST include in the header

class UStaticMeshComponent;

UCLASS()
class YOURPROJECT_API AMovingPlatform : public AActor  // replace YOURPROJECT_API (see step 4)
{
	GENERATED_BODY()

public:
	AMovingPlatform();

	virtual void Tick(float DeltaTime) override;

protected:
	virtual void BeginPlay() override;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Moving Platform")
	TObjectPtr<UStaticMeshComponent> Mesh;

	// Where to go, measured from the platform itself. MakeEditWidget adds a draggable diamond.
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Moving Platform", meta = (MakeEditWidget = true))
	FVector TargetOffset = FVector(0.f, 400.f, 0.f);

	// Seconds to travel from one end to the other
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Moving Platform", meta = (ClampMin = "0.1"))
	float TravelTime = 3.f;

	// Seconds to wait at each end
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Moving Platform", meta = (ClampMin = "0.0"))
	float PauseTime = 1.f;

	// true = slow down gently at the ends, false = constant speed
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Moving Platform")
	bool bSmoothMotion = true;

private:
	FVector StartLocation = FVector::ZeroVector;
	FVector EndLocation = FVector::ZeroVector;
	float CycleTimer = 0.f;
};

// ===== MovingPlatform.cpp =====
#include "MovingPlatform.h"
#include "Components/StaticMeshComponent.h"

AMovingPlatform::AMovingPlatform()
{
	PrimaryActorTick.bCanEverTick = true;

	Mesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Mesh"));
	RootComponent = Mesh;
	Mesh->SetMobility(EComponentMobility::Movable);
}

void AMovingPlatform::BeginPlay()
{
	Super::BeginPlay();

	StartLocation = GetActorLocation();
	// TargetOffset is "local" (relative to the platform), so turn it into a world position
	EndLocation = GetActorTransform().TransformPosition(TargetOffset);
}

void AMovingPlatform::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);

	const float SafeTravelTime = FMath::Max(TravelTime, 0.1f);
	const float SafePauseTime = FMath::Max(PauseTime, 0.f);

	// One full cycle = go there, pause, come back, pause
	const float LegTime = SafeTravelTime + SafePauseTime;
	const float CycleTime = LegTime * 2.f;
	CycleTimer = FMath::Fmod(CycleTimer + DeltaTime, CycleTime);

	// Alpha: 0 = at the start, 1 = at the target
	float Alpha = 0.f;
	if (CycleTimer < SafeTravelTime)
	{
		Alpha = CycleTimer / SafeTravelTime;                          // going out
	}
	else if (CycleTimer < LegTime)
	{
		Alpha = 1.f;                                                  // waiting at the target
	}
	else if (CycleTimer < LegTime + SafeTravelTime)
	{
		Alpha = 1.f - (CycleTimer - LegTime) / SafeTravelTime;        // coming back
	}
	else
	{
		Alpha = 0.f;                                                  // waiting at the start
	}

	if (bSmoothMotion)
	{
		Alpha = FMath::InterpEaseInOut(0.f, 1.f, Alpha, 2.f);
	}

	// Lerp = "blend" between two positions using Alpha
	SetActorLocation(FMath::Lerp(StartLocation, EndLocation, Alpha));
}
