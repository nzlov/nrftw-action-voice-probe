PROJECT := nrftw-action-voice-probe.csproj
CONFIG := Release
TARGET_FRAMEWORK := net6.0
PLUGIN_NAME := NRFTW.ActionVoiceProbe
DLL_SOURCE := bin/$(CONFIG)/$(TARGET_FRAMEWORK)/nrftw-action-voice-probe.dll
DIST_DIR := dist
PACKAGE_DIR := $(DIST_DIR)/$(PLUGIN_NAME)
PACKAGE_ZIP := $(DIST_DIR)/$(PLUGIN_NAME).zip
WWISE_BANK_SOURCE := ActionVoiceProbe/GeneratedSoundBanks/Windows/ActionVoiceProbe.bnk
WWISE_BANK_DEST := $(PACKAGE_DIR)/Mods/$(PLUGIN_NAME)/wwise/ActionVoiceProbe.bnk
WWISE_BANK_VALIDATOR := scripts/verify-wwise-bank.pl

.PHONY: build package clean

build:
	dotnet build $(PROJECT) -c $(CONFIG)

package: build
	rm -rf $(PACKAGE_DIR) $(PACKAGE_ZIP)
	mkdir -p $(PACKAGE_DIR)/Mods/$(PLUGIN_NAME)/wwise
	cp $(DLL_SOURCE) $(PACKAGE_DIR)/Mods/$(PLUGIN_NAME)/$(PLUGIN_NAME).dll
	cp bindings.ini $(PACKAGE_DIR)/Mods/$(PLUGIN_NAME)/bindings.ini
	cp BINDING_KEYS.md $(PACKAGE_DIR)/Mods/$(PLUGIN_NAME)/BINDING_KEYS.md
	cp manifest.json $(PACKAGE_DIR)/Mods/$(PLUGIN_NAME)/manifest.json
	perl $(WWISE_BANK_VALIDATOR) $(WWISE_BANK_SOURCE)
	cp $(WWISE_BANK_SOURCE) $(WWISE_BANK_DEST)
	cd $(DIST_DIR) && zip -r $(PLUGIN_NAME).zip $(PLUGIN_NAME)

clean:
	dotnet clean $(PROJECT) -c $(CONFIG)
	rm -rf $(DIST_DIR)
