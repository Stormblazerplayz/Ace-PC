/*
  ROTATING ACTOR  (Unreal Engine 5 C++)
  An actor that spins forever (and can also float up and down) - great for
  coins, power-ups, fans and spinning obstacles.

  THIS FILE HOLDS TWO FILES: the header (.h) and the source (.cpp). Split them like this:
  1. In Unreal: Tools > New C++ Class > Actor > Next > name it RotatingActor > Create Class.
  2. Open RotatingActor.h and replace EVERYTHING with the part under "RotatingActor.h" below.
  3. Open RotatingActor.cpp and replace EVERYTHING with the part under "RotatingActor.cpp" below.
  4. IMPORTANT: change YOURPROJECT_API to your project's name in CAPITALS + _API.
     Example: project "SpaceGame" -> SPACEGAME_API (it's in the file Unreal made for you).
  5. Compile (Ctrl+Alt+F11 for Live Coding, or Build in Visual Studio / Rider).
  6. Drag "RotatingActor" from the Content Browser (C++ Classes folder) into your level,
     then pick a Static Mesh for it in the Details panel.
*/

// ===== RotatingActor.h =====
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "RotatingActor.generated.h"  // must be the LAST include in the header

class UStaticMeshComponent;

UCLASS()
class YOURPROJECT_API ARotatingActor : public AActor  // replace YOURPROJECT_API (see step 4)
{
	GENERATED_BODY()

public:
	ARotatingActor();

	virtual void Tick(float DeltaTime) override;

protected:
	virtual void BeginPlay() override;

	// The 3D model you see
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Rotating")
	TObjectPtr<UStaticMeshComponent> Mesh;

	// Degrees per second. FRotator is (Pitch, Yaw, Roll). Yaw spins like a top.
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Rotating")
	FRotator RotationSpeed = FRotator(0.f, 90.f, 0.f);

	// true = spin around its own axes, false = spin around the world's axes
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Rotating")
	bool bSpinInLocalSpace = true;

	// How far it floats up and down (in cm). 0 = no floating.
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Rotating|Bobbing", meta = (ClampMin = "0.0"))
	float BobHeight = 0.f;

	// How fast it floats up and down
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Rotating|Bobbing")
	float BobSpeed = 2.f;

private:
	FVector StartLocation = FVector::ZeroVector;
	float RunningTime = 0.f;
};

// ===== RotatingActor.cpp =====
#include "RotatingActor.h"
#include "Components/StaticMeshComponent.h"

ARotatingActor::ARotatingActor()
{
	// Tick = run code every frame. We need it to keep spinning.
	PrimaryActorTick.bCanEverTick = true;

	Mesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Mesh"));
	RootComponent = Mesh;
	Mesh->SetMobility(EComponentMobility::Movable);  // things that move must be Movable
}

void ARotatingActor::BeginPlay()
{
	Super::BeginPlay();
	StartLocation = GetActorLocation();  // remember where we started (for bobbing)
}

void ARotatingActor::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);

	// Multiply by DeltaTime so it spins at the same speed on fast and slow computers
	const FRotator Step = RotationSpeed * DeltaTime;
	if (bSpinInLocalSpace)
	{
		AddActorLocalRotation(Step);
	}
	else
	{
		AddActorWorldRotation(Step);
	}

	if (BobHeight > 0.f)
	{
		RunningTime += DeltaTime;
		const float Offset = FMath::Sin(RunningTime * BobSpeed) * BobHeight;
		SetActorLocation(StartLocation + FVector(0.f, 0.f, Offset));
	}
}
