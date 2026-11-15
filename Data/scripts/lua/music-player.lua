--[[
	BACKGROUND MUSIC PLAYER (playlist + mute key)
	Plays every song in a folder, one after another, forever.
	Press M to mute or unmute.

	HOW TO USE:
	1. In SoundService, insert a Folder named: Music
	2. Insert Sound objects into the folder and set each one's SoundId
	   (for example rbxassetid://1234567890 - use audio you are allowed to use).
	3. Put this LocalScript in StarterPlayer > StarterPlayerScripts.
	Because it's a LocalScript, each player hears it on their own and can mute it.
]]

local SoundService = game:GetService("SoundService")
local UserInputService = game:GetService("UserInputService")

local musicFolder = SoundService:WaitForChild("Music")

-- SETTINGS
local SHUFFLE = true -- true = random order
local MUTE_KEY = Enum.KeyCode.M
local GAP_BETWEEN_SONGS = 1 -- seconds of quiet between songs
local MAX_WAIT_TO_LOAD = 10 -- skip a song if it won't load after this many seconds

local muted = false
local currentSound = nil

local function getSongs()
	local songs = {}
	for _, object in musicFolder:GetChildren() do
		if object:IsA("Sound") then
			object.Looped = false
			if object:GetAttribute("NormalVolume") == nil then
				object:SetAttribute("NormalVolume", object.Volume)
			end
			table.insert(songs, object)
		end
	end
	return songs
end

local function applyMute()
	if currentSound then
		local normalVolume = currentSound:GetAttribute("NormalVolume") or 0.5
		currentSound.Volume = muted and 0 or normalVolume
	end
end

UserInputService.InputBegan:Connect(function(input, gameProcessed)
	if gameProcessed then
		return -- they were typing in chat
	end
	if input.KeyCode == MUTE_KEY then
		muted = not muted
		applyMute()
	end
end)

-- Plays one song and waits until it ends (or gives up if it never loads)
local function playAndWait(sound)
	currentSound = sound
	applyMute()

	local finished = false
	local connection = sound.Ended:Connect(function()
		finished = true
	end)

	sound:Play()

	local startTime = os.clock()
	while not finished do
		task.wait(0.5)
		if not sound.IsLoaded and os.clock() - startTime > MAX_WAIT_TO_LOAD then
			warn("Song would not load, skipping: " .. sound.Name)
			break
		end
		if not sound.Parent then
			break -- the song was deleted
		end
	end

	connection:Disconnect()
	sound:Stop()
end

while true do
	local songs = getSongs()
	if #songs == 0 then
		task.wait(2) -- no songs yet, check again soon
	else
		if SHUFFLE then
			-- Mix up the list (Fisher-Yates shuffle)
			for i = #songs, 2, -1 do
				local j = math.random(1, i)
				songs[i], songs[j] = songs[j], songs[i]
			end
		end
		for _, sound in songs do
			playAndWait(sound)
			task.wait(GAP_BETWEEN_SONGS)
		end
	end
end
