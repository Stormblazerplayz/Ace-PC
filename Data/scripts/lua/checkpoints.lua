--[[
	OBBY CHECKPOINTS (with a "Stage" leaderboard)
	Touch a checkpoint to save your progress. When you die, you come back
	at the last checkpoint you reached.

	HOW TO USE:
	1. In Workspace, insert a Folder and name it: Checkpoints
	2. Put your checkpoint Parts inside it, named with numbers: 1, 2, 3, 4 and so on
	   (1 is the start, 2 is the next one, and so on). Make them Anchored.
	3. Keep one normal SpawnLocation at the start of your obby.
	4. Put this Script in ServerScriptService.
	Works together with the "datastore-save" script to remember stages after leaving.
]]

local Players = game:GetService("Players")

local checkpointsFolder = workspace:WaitForChild("Checkpoints")

-- SETTINGS
local ALLOW_SKIPPING = false -- false = you must touch checkpoints in order (stops cheaters skipping ahead)
local HEIGHT_ABOVE_CHECKPOINT = 4 -- how high above the checkpoint you appear

-- Gets (or makes) the "leaderstats" folder and the "Stage" number for a player
local function getStage(player)
	local leaderstats = player:FindFirstChild("leaderstats")
	if not leaderstats then
		leaderstats = Instance.new("Folder")
		leaderstats.Name = "leaderstats" -- this exact name shows it on the player list
		leaderstats.Parent = player
	end

	local stage = leaderstats:FindFirstChild("Stage")
	if not stage then
		stage = Instance.new("IntValue")
		stage.Name = "Stage"
		stage.Value = 1
		stage.Parent = leaderstats
	end
	return stage
end

-- Moves a player's character to the checkpoint that matches their stage
local function sendToCheckpoint(player)
	local character = player.Character
	if not character or not character:FindFirstChild("HumanoidRootPart") then
		return
	end

	local stage = getStage(player)
	local checkpoint = checkpointsFolder:FindFirstChild(tostring(stage.Value))
	if checkpoint and checkpoint:IsA("BasePart") then
		local spot = checkpoint.Position + Vector3.new(0, checkpoint.Size.Y / 2 + HEIGHT_ABOVE_CHECKPOINT, 0)
		character:PivotTo(CFrame.new(spot))
	end
end

local function onCharacterAdded(player, character)
	-- Wait until the body is ready, then one tiny wait so Roblox finishes spawning
	character:WaitForChild("HumanoidRootPart")
	task.wait()
	sendToCheckpoint(player)
end

local function onPlayerAdded(player)
	getStage(player)

	player.CharacterAdded:Connect(function(character)
		onCharacterAdded(player, character)
	end)

	-- If the save script loads an old stage, send them there right away
	player:GetAttributeChangedSignal("DataLoaded"):Connect(function()
		sendToCheckpoint(player)
	end)

	if player.Character then
		task.spawn(onCharacterAdded, player, player.Character)
	end
end

-- When someone touches a checkpoint part
local function setUpCheckpoint(checkpoint)
	if not checkpoint:IsA("BasePart") then
		return
	end
	local stageNumber = tonumber(checkpoint.Name)
	if not stageNumber then
		warn("Checkpoint named '" .. checkpoint.Name .. "' is not a number, so it is skipped.")
		return
	end

	checkpoint.Touched:Connect(function(hit)
		local character = hit:FindFirstAncestorOfClass("Model")
		local player = character and Players:GetPlayerFromCharacter(character)
		if not player then
			return
		end
		local humanoid = character:FindFirstChildOfClass("Humanoid")
		if not humanoid or humanoid.Health <= 0 then
			return
		end

		local stage = getStage(player)
		-- Debounce: only change the stage if this is a NEW checkpoint for them
		local isNext = stageNumber == stage.Value + 1
		local isAhead = stageNumber > stage.Value
		if isNext or (ALLOW_SKIPPING and isAhead) then
			stage.Value = stageNumber
		end
	end)
end

for _, checkpoint in checkpointsFolder:GetChildren() do
	setUpCheckpoint(checkpoint)
end
checkpointsFolder.ChildAdded:Connect(setUpCheckpoint)

Players.PlayerAdded:Connect(onPlayerAdded)
for _, player in Players:GetPlayers() do
	onPlayerAdded(player)
end
