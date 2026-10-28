#!/usr/bin/env perl
use strict;
use warnings;

# GLUE: Validate the cross-project ShortID contract until the bank can be built
# from the game's original Wwise project.
my $master_audio_bus_id = 3803692087;
my $vox_player_bus_id = 610960780;
my $pitch_rtpc_id = 3369551963;
my $supported_bank_version = 145;

die "Usage: $0 <bank.bnk>\n" unless @ARGV == 1;
my ($input_path) = @ARGV;

open my $input, '<:raw', $input_path or die "Cannot open $input_path: $!\n";
local $/;
my $bank = <$input>;
close $input or die "Cannot close $input_path: $!\n";
die "Cannot read $input_path\n" unless defined $bank;

my ($hirc_start, $hirc_end);
my $bank_version;
my $offset = 0;
while ($offset + 8 <= length $bank) {
    my $tag = substr($bank, $offset, 4);
    my $size = unpack 'V', substr($bank, $offset + 4, 4);
    my $payload_start = $offset + 8;
    my $payload_end = $payload_start + $size;
    die "Invalid $tag chunk size in $input_path\n" if $payload_end > length $bank;

    if ($tag eq 'BKHD') {
        die "Multiple BKHD chunks in $input_path\n" if defined $bank_version;
        die "BKHD chunk is too small in $input_path\n" if $size < 4;
        $bank_version = unpack 'V', substr($bank, $payload_start, 4);
        die "Unsupported Wwise bank version $bank_version; expected $supported_bank_version\n"
            if $bank_version != $supported_bank_version;
    }
    elsif ($tag eq 'HIRC') {
        die "Multiple HIRC chunks in $input_path\n" if defined $hirc_start;
        ($hirc_start, $hirc_end) = ($payload_start, $payload_end);
    }

    $offset = $payload_end;
}

die "Trailing or truncated chunk data in $input_path\n" if $offset != length $bank;
die "Missing BKHD chunk in $input_path\n" unless defined $bank_version;
die "Missing HIRC chunk in $input_path\n" unless defined $hirc_start && defined $hirc_end;
die "HIRC chunk is too small in $input_path\n" if $hirc_start + 4 > $hirc_end;

my $master_id = pack 'V', $master_audio_bus_id;
my $vox_id = pack 'V', $vox_player_bus_id;
my $pitch_id = pack 'V', $pitch_rtpc_id;
my $object_count = unpack 'V', substr($bank, $hirc_start, 4);
my $position = $hirc_start + 4;
my $sound_count = 0;
my $routed_mixer_count = 0;
my $master_reference_count = 0;

for (1 .. $object_count) {
    die "Truncated HIRC object header in $input_path\n" if $position + 5 > $hirc_end;

    my $type = unpack 'C', substr($bank, $position, 1);
    my $size = unpack 'V', substr($bank, $position + 1, 4);
    my $body_start = $position + 5;
    my $body_end = $body_start + $size;
    die "Truncated HIRC object body in $input_path\n" if $body_end > $hirc_end;

    my $body = substr($bank, $body_start, $size);
    my @master_offsets = find_offsets($body, $master_id);
    my @vox_offsets = find_offsets($body, $vox_id);
    my @pitch_offsets = find_offsets($body, $pitch_id);
    $master_reference_count += scalar @master_offsets;
    $sound_count++ if $type == 2;

    if ($type == 7 && @vox_offsets == 1 && @pitch_offsets == 1 && @master_offsets == 0) {
        $routed_mixer_count++;
    }

    $position = $body_end;
}

die "HIRC object table does not consume the complete chunk in $input_path\n" if $position != $hirc_end;
die "No Sound objects found in $input_path\n" if $sound_count == 0;
die "Expected one ActorMixer routed to VOX_Player with the attack Pitch RTPC; found $routed_mixer_count\n"
    if $routed_mixer_count != 1;
die "Content bank still contains $master_reference_count Master Audio Bus reference(s)\n"
    if $master_reference_count != 0;

print "Verified Wwise bank version $bank_version: sounds=$sound_count, "
    . "VOX_Player=$vox_player_bus_id, pitchRTPC=$pitch_rtpc_id.\n";

sub find_offsets {
    my ($value, $needle) = @_;
    my @offsets;
    my $search_from = 0;

    while (1) {
        my $found = index $value, $needle, $search_from;
        last if $found < 0;
        push @offsets, $found;
        $search_from = $found + length $needle;
    }

    return @offsets;
}
