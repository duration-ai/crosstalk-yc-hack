// Polyfork model selection for Dogpatch. Every building, tree and prop variant the game uses is listed
// here: the Polyfork asset, the knobs it is built with (colours from the Speed Dial palette), and why.
// bake.mjs builds each variant from the asset's own module, so recolouring goes through the model's
// own colour knobs, and writes manifest.json (this list resolved, with baked sizes) beside it.

export const palette = {
  sky: '#1C9BF2', bay: '#17B2D0', cloud: '#FFFFFF', asphalt: '#A9ADB4', shadow: '#6C758B', shade: '#A3AEC4',
  brick: '#E2603F', brickDeep: '#C94A32', sunshine: '#FFCB1F', crane: '#FFC21A', lemon: '#FFE27A', mint: '#8CE3BF',
  coral: '#FF9C86', lilac: '#BBA6F7', powder: '#9ED7FF', cream: '#FFF3DA', hotline: '#FF3E8E', ink: '#1C2347',
  navy: '#2C4FA3', sidewalk: '#EEE7DA', tree: '#48C26A', treeDeep: '#2FA35A',
  // Not in the guide's table: one warm brown for trunks and raw timber, which the key art shows but never names.
  bark: '#8B5A3C',
};
export const derived = ['bark'];

const victorian = ['lemon', 'mint', 'coral', 'lilac', 'powder', 'cream'];
const variants = [];
function add(role, asset, key, knobs, why) { variants.push({ key, role, asset, knobs, why }); }

// ---- Buildings ----------------------------------------------------------------------------------
for (const c of victorian)
  add('building:victorian-low', 'townhouse-row-unit-b9544b', 'b-rowhouse-' + c,
    { piece: 'mid', lights: 2, siding: c, brick: c === 'cream' ? 'coral' : 'cream', trim: 'cloud', roof: 'shadow', pane: 'navy', door: 'ink', plinth: 'sidewalk', stoop: 'cloud', interior: 'ink' },
    'Neighborhood Pathways row unit: a 4 m terrace module with blank party walls, so units butt along a block like SF rowhouses. Two storeys (7.5 m) for the low Victorians and post-war rowhouses; pastel upper storey over a cream base per the guide.');
for (const c of victorian)
  for (const floors of [3, 4])
    add('building:victorian', 'brownstone-row-house-1b84fd', 'b-italianate-' + c + '-' + floors,
      { floors, entrance: 'stoop', areaway: false, hoods: 'cap', brackets: 6, fireEscape: false, waterTank: false, tone: 0.15, stone: c, brick: c, soot: 'shade', trim: 'cream', sash: 'cream', door: 'ink', glass: 'navy', roof: 'shadow', tar: 'ink', dark: 'ink', timber: 'cream' },
      'Closest catalogue match to an SF Italianate Victorian: flat front, bracketed cornice, two-window bay and a raised stoop on an 8 m lot. Fire escape and water tank are switched off (New York details); facade painted in the six Victorian pastels with cream trim and navy windows.');
for (const c of ['cream', 'lemon', 'coral', 'powder'])
  for (const floors of [4, 6])
    add('building:apartment', 'slab-apartment-block-1b02a2', 'b-condo-' + c + '-' + floors,
      { floors, bays: 1, groundFloor: 'lobby', shell: c, trim: 'cloud', reveal: 'cloud', recess: 'shade', glass: 'navy', light: c === 'lemon' ? 'cream' : 'lemon', base: 'ink' },
      'Modern flat-roofed residential block with ribbon windows, the shape of the post-2000 Dogpatch condos; floors chosen from DataSF height.');
for (const c of ['brick', 'brickDeep', 'cream'])
  for (const floors of [3, 5])
    add('building:loft', 'cast-iron-loft-b04b5a', 'b-loft-' + c + '-' + floors,
      { floors, archHead: 'segmental', fireEscape: false, waterTank: false, tone: 0.25, front: c, trim: 'cream', wall: c === 'cream' ? 'shade' : 'brickDeep', soot: 'shadow', iron: 'ink', frame: 'ink', glass: 'navy', door: 'ink', roof: 'shadow', tar: 'ink', metal: 'shade', timber: 'cream' },
      'Pre-war masonry loft with arched bays: older mixed-use and office buildings along 3rd and Illinois. Brick in the Pier 70 reds or cream stucco.');
for (const c of ['cream', 'coral'])
  for (const floors of [4, 6])
    add('building:midrise', 'department-store-ffbd58', 'b-midrise-' + c + '-' + floors,
      { floors, display: 'empty', cornice: 'plain', waterTank: false, awnings: false, tone: 0.3, masonry: c, trim: 'cloud', iron: 'ink', frame: 'ink', glass: 'navy', door: 'ink', sign: 'lemon', canvas: 'cream', roof: 'shadow', tar: 'ink', dark: 'shadow', party: 'shade', timber: 'cream', mannequin: 'coral', paper: 'cream' },
      'Large-plate mid-rise with a regular window grid for wide mixed-use and office blocks.');
for (const c of ['brick', 'coral', 'lemon'])
  for (const floors of [1, 2])
    add('building:corner-shop', 'corner-shop-5aec8c', 'b-cornershop-' + c + '-' + floors,
      { floors, entrance: 'corner', bayWindows: 'paired', awnings: true, tone: 0.25, brick: c, trim: 'cream', plinth: 'ink', sign: 'cloud', frame: 'ink', panel: 'shade', glass: 'navy', shopGlass: 'navy', roof: 'shadow', tar: 'ink', iron: 'ink', interior: 'ink', metal: 'shade', party: c === 'brick' ? 'brickDeep' : 'shade', soot: 'shadow', awning: 'mint', awningStripe: 'cream' },
      'Corner shop with a cut corner entrance: one- and two-storey retail on street corners (22nd and 3rd).');
for (const c of ['brick', 'cream'])
  for (const bays of [2, 3, 4])
    add('building:shop-row', 'one-storey-shop-row-83c958', 'b-shoprow-' + c + '-' + bays,
      { bays, shutter: 'open', awnings: true, tone: 0.25, brick: c, trim: 'cream', plinth: 'ink', frame: 'ink', glass: 'navy', sign: 'cream', steel: 'shade', roof: 'shadow', tar: 'ink', party: 'shade', course: 'shadow', iron: 'ink', awning1: 'coral', awning2: 'powder', awning3: 'mint', awning4: 'lemon' },
      'One-storey retail frontage in 4 m bays for single-storey commercial buildings.');
for (const c of ['shade', 'cream'])
  for (const floors of [1, 2, 3])
    add('building:workshop', 'auto-repair-garage-b91464', 'b-workshop-' + c + '-' + floors,
      { floors, shutter: 'closed', patches: 0.2, tone: 0.25, brick: c, trim: 'cloud', sign: 'cream', plinth: 'shadow', frame: 'ink', door: 'sunshine', glass: 'navy', steel: 'asphalt', roof: 'shadow', tar: 'ink', concrete: 'sidewalk', interior: 'ink', equip: 'coral', party: 'shadow', course: 'shadow' },
      'Concrete light-industrial building with roll-up doors: the PDR workshops, auto shops and studios that fill Dogpatch blocks.');
for (const c of ['asphalt', 'powder', 'cream', 'mint'])
  add('building:metal-shed', 'skate-hall-bay-197a78', 'b-metalshed-' + c,
    { piece: 'end', cladding: 'corrugated', pitch: 8, kickWall: 0.6, windows: true, sign: false, frame: 'ink', walls: c, roof: 'shadow', trim: 'cloud', concrete: 'sidewalk', door: 'sunshine', glass: 'navy', polycarb: 'cloud', liner: 'shade' },
    'Corrugated steel portal-frame hall with a roll-up door: the catalogue\'s nearest modern warehouse (reported as a gap: no light-industrial metal building).');
for (const c of ['brick', 'brickDeep'])
  for (const bays of [3, 5])
    for (const storeys of [1, 2, 3])
      add('building:brick-industrial', 'brick-depot-warehouse-3dd642', 'b-depot-' + c + '-' + bays + 'x' + storeys,
        { bays, storeys, damage: 0, rafters: 7, tone: 0.3, brick: c, dressing: 'cream', iron: 'ink', pane: 'navy', timber: 'lemon', deck: 'shade', inside: 'shadow' },
        'Brick freight depot with stone string courses and freight doors: the historic brick industrial buildings (Pier 70, American Industrial Center era). Damage off; bays and storeys picked per footprint.');
for (const c of ['brick', 'brickDeep'])
  for (const length of [16, 32])
    add('building:brick-hall', 'engine-shed-eff627', 'b-hall-' + c + '-' + length,
      { doors: 1, length, roofPitch: 30, wear: 0.1, brick: c, stone: 'cream', slate: 'shadow', iron: 'ink', door: 'lemon', glass: 'navy', interior: 'ink', floor: 'shade' },
      'Gabled brick hall 12 m wide: tiled side by side it reads as the multi-gable shipyard sheds of Pier 70 (Buildings 2, 12, 113).');
for (const c of ['asphalt', 'powder'])
  add('building:shed', 'backyard-garden-shed-38d79f', 'b-shed-' + c,
    { material: 'ribbed-metal', doorState: 'closed', stripes: 'plain', boardA: c, boardB: c, boardC: c, boardD: c, trim: 'ink', door: 'navy', roof: 'shadow', ridge: 'ink', interior: 'ink', metal: 'cloud' },
    'Small ribbed-metal shed for footprints under 60 m2 and 4.5 m.');
for (const c of ['cream', 'mint'])
  add('building:garage', 'detached-garage-38a178', 'b-garage-' + c,
    { doorState: 'closed', siding: c, trim: 'cloud', doorLeaf: 'cloud', roof: 'shadow', glass: 'navy', interior: 'ink' },
    'Single garage for small residential outbuildings.');
for (const decks of [3, 5])
  add('building:parking', 'parking-garage-6e4f2c', 'b-parking-' + decks,
    { decks, parapetInfill: 'solid-band', waterTank: false, tone: 0.2, concrete: 'shade', trim: 'cloud', deck: 'shadow', tar: 'ink', interior: 'asphalt', steel: 'navy', accent: 'sunshine', lamp: 'cream', party: 'shadow', course: 'shadow', iron: 'ink', lines: 'sunshine', timber: 'bark' },
    'Open-deck parking structure.');
for (const floors of [3, 4])
  add('building:school', 'public-school-19ade5', 'b-school-' + floors,
    { floors, parapet: 'plain', quoins: true, tone: 0.25, brick: 'brick', stone: 'cream', glass: 'navy', frame: 'ink', door: 'lemon', deck: 'shadow', seam: 'ink', patch: 'cream', vent: 'asphalt', party: 'brickDeep', course: 'brickDeep' },
    'Masonry school for civic and institutional buildings.');
add('building:church', 'brick-church-322885', 'b-church', { towerHeight: 15, towerTop: 'pyramid', tone: 0.25, brick: 'cream', stone: 'cloud', roof: 'shadow', metal: 'shadow', door: 'ink', glass: 'navy', interior: 'ink', party: 'shade', course: 'shade' }, 'Church (OSM amenity=place_of_worship).');
add('building:fire-station', 'fire-station-e2b756', 'b-firestation', { floors: 2, bayDoor: 'closed', brick: 'brick', stone: 'cream', door: 'coral', frame: 'ink', glass: 'navy', roof: 'shadow', metal: 'shade', dark: 'ink', party: 'brickDeep', course: 'brickDeep' }, 'Fire station (OSM amenity=fire_station).');
for (const height of [8, 14])
  add('building:tank', 'fuel-farm-tank-ed67f0', 'b-tank-' + height, { height, wear: 0.1, shell: 'cloud', roof: 'cream', flange: 'shade', concrete: 'sidewalk', steel: 'asphalt', grating: 'shadow' }, 'Round storage tank for OSM man_made=storage_tank / tank footprints.');
for (const floors of [6, 10])
  add('building:tower', 'office-tower-1538cc', 'b-tower-' + floors, { floors, lobby: 'double', waterTank: false, concrete: 'cream', ledge: 'cloud', glass: 'navy', spandrel: 'shade', frame: 'ink', roof: 'shadow', tar: 'ink', roofgear: 'asphalt', iron: 'ink', timber: 'cream', party: 'shade', course: 'shade', interior: 'ink' }, 'Slender office tower for the few buildings above 25 m on a small plate.');

// ---- Trees ----------------------------------------------------------------------------------------
for (const crown of ['egg', 'round', 'broad'])
  for (const c of ['tree', 'treeDeep'])
    add('tree:street', 'street-tree-0d33f3', 't-street-' + crown + '-' + c,
      { crown, tallness: 1, spread: 1, canopy: c, bark: 'bark', frame: 'sidewalk', bed: 'treeDeep' },
      'NYC kit street tree in its square pit (272 tris): evergreen and most broadleaf street trees. Crown profile per species form (egg upright, round, broad).');
for (const c of ['tree', 'treeDeep'])
  add('tree:shade', 'broad-canopy-shade-tree-e7c4a2', 't-shade-' + c,
    { season: 'summer', foliage: c, foliageHi: c === 'tree' ? 'mint' : 'tree', bark: 'bark' },
    'Wide lobed canopy for large deciduous species (London plane, sycamore, elm, ash).');
add('tree:ornamental', 'ornamental-front-yard-tree-c6781a', 't-ornamental', { season: 'summer', foliage: 'tree', foliageHi: 'mint', bark: 'bark', edging: 'sidewalk', mulch: 'bark' }, 'Small rounded ornamental for flowering and small species (plum, cherry, crape myrtle, maple, magnolia).');
for (const height of [9, 14])
  add('tree:palm', 'chilean-palm-45fef5', 't-palm-' + height, { height, fronds: 18, deadFronds: 2, wear: 0.1, leaf: 'tree', underside: 'treeDeep', deadLeaf: 'lemon', bark: 'shade', leafBase: 'bark' }, 'Feather palm: Canary Island date palm and the other palms on the street tree list.');
add('tree:conifer', 'tall-pine-tree-ab4108', 't-conifer', { season: 'summer', tallness: 9, needles: 'treeDeep', bark: 'bark', snow: 'cloud' }, 'Conifer for pines, cypresses, redwoods and Norfolk Island pine.');

// ---- Props ---------------------------------------------------------------------------------------
add('prop:street_lamp', 'cobra-head-street-light-3e25b8', 'p-lamp', { arm: 'single', lamp: 'led', pole: 'shade', head: 'asphalt', lens: 'cream', ledPanel: 'ink', base: 'sidewalk', door: 'shadow' }, 'Cobra-head street light, the standard SF fixture.');
add('prop:traffic_signal', 'traffic-light-b217a2', 'p-signal', { armSignal: true, pedSignal: true, paint: 'ink', dark: 'ink', stop: 'brick', amber: 'sunshine', go: 'mint', walk: 'cream' }, 'Mast-arm traffic signal with pedestrian head.');
add('prop:stop', 'street-sign-post-c621b4', 'p-stop', { face: 'stop', base: 'sleeve', post: 'shade', foot: 'shadow', plate: 'brick', border: 'cloud', legend: 'cloud', blade: 'navy', bladeLegend: 'cloud', hardware: 'ink' }, 'Stop sign on a post (face knob = stop).');
add('prop:give_way', 'street-sign-post-c621b4', 'p-signpost', { face: 'streetName', base: 'sleeve', post: 'shade', foot: 'shadow', plate: 'navy', border: 'cloud', legend: 'cloud', blade: 'navy', bladeLegend: 'cloud', hardware: 'ink' }, 'Generic sign post for give-way and other OSM traffic signs (the catalogue has no yield face).');
add('prop:fire_hydrant', 'fire-hydrant-b68b17', 'p-hydrant', { facets: 8, body: 'cloud', iron: 'shadow', reveal: 'sidewalk', capNut: 'cream', crownNut: 'navy' }, 'Hydrant; SF hydrants are white-bodied with a coloured bonnet.');
add('prop:bench', 'park-bench-dc8b62', 'p-bench', { iron: 'ink', wood: 'bark', armrests: true }, 'Park bench.');
add('prop:bus_shelter', 'bus-shelter-f786b4', 'p-shelter', { bays: 2, adState: 'poster', seating: true, frame: 'shadow', glass: 'powder', bench: 'ink', adCase: 'ink', diffuser: 'cream', posterSky: 'sky', posterLand: 'bay', posterMotif: 'sunshine' }, 'Bus shelter for stops tagged shelter=yes.');
add('prop:bus_stop', 'street-sign-pole-1e9551', 'p-busstop', { finial: 'cap', iron: 'ink', blade: 'sunshine', trim: 'cloud', ink: 'ink' }, 'Stop pole with a yellow blade, like a Muni stop marker, for stops without a shelter.');
add('prop:bollard', 'sidewalk-bollard-c2567e', 'p-bollard', { style: 'steel-pipe', bands: 1, footing: 'plinth', body: 'shade', cap: 'shadow', band: 'sunshine', iron: 'ink', concrete: 'sidewalk' }, 'Steel pipe bollard.');
add('prop:bike_rack', 'bike-rack-a1e7c3', 'p-bikerack', { hoops: 3, railBar: true, paint: 'shadow', plate: 'sidewalk', bolt: 'ink' }, 'Hoop bike rack; also used for bike-share docks (no dock model in the catalogue).');
add('prop:parking_meter', 'parking-meter-e9cf71', 'p-meter', { head: 'single', post: 'shadow', shell: 'shade', face: 'navy', dial: 'cream', ring: 'asphalt', plate: 'sidewalk', hardware: 'ink' }, 'Single-head parking meter.');
add('prop:trash_can', 'metal-trash-can-f57043', 'p-bin', { lid: 'closed', load: 'empty', steel: 'treeDeep', body: 'treeDeep', hardware: 'ink', inside: 'ink', bag: 'ink' }, 'Street litter can in SF Public Works green.');
add('prop:post_box', 'mailbox-8e36e8', 'p-mailbox', { placard: true, shell: 'navy', hardware: 'ink', accent: 'cloud' }, 'USPS collection box.');
add('prop:drinking_water', 'park-drinking-fountain-b1c25b', 'p-fountain', { concrete: 'sidewalk', basin: 'shade', metal: 'shadow', slot: 'ink', button: 'cloud', lime: 'mint' }, 'Drinking fountain.');
add('prop:picnic_table', 'picnic-table-4ee33a', 'p-picnic', { wood: 'bark', frame: 'ink', rail: 'shadow', bolt: 'asphalt' }, 'Picnic table.');
add('prop:vending', 'newspaper-box-3f2c0e', 'p-newsbox', { body: 'powder', sign: 'cream', stand: 'ink', frame: 'shade', hardware: 'shadow', glass: 'navy', paper: 'cloud', print: 'shadow', interior: 'ink' }, 'Newspaper/vending box.');
add('prop:charging_station', 'vehicle-charging-post-45cbb8', 'p-charger', { head: 'cable', shell: 'cloud', base: 'ink', strip: 'mint', screen: 'navy', cable: 'shade' }, 'EV charging post.');
add('prop:power_pole', 'timber-utility-pole-a93693', 'p-pole', { piece: 'straight', wires: 'two', gear: 'bare', pole: 'bark', butt: 'bark', arm: 'cream', steel: 'shade', insul: 'ink', can: 'asphalt', box: 'shade', wire: 'ink' }, 'Timber utility pole.');
add('prop:flagpole', 'flag-pole-015e53', 'p-flagpole', { arrangement: 1, pole: 'shade', collar: 'shadow', fittings: 'shadow', truck: 'asphalt', halyard: 'cream', cloth: 'navy', hem: 'cloud' }, 'Flagpole.');
add('prop:crane_mast', 'tower-crane-mast-1784a3', 'p-crane-mast', { height: 8, ties: true, guardHurdle: 'none', accessLadder: 'none', wear: 0.1, steel: 'crane', base: 'ink', ladder: 'shade', ballast: 'asphalt', concrete: 'sidewalk', mud: 'shadow', hurdle: 'coral' }, 'Crane mast in crane yellow for the Pier 70 cranes (the catalogue has no shipyard whirley/gantry crane; reported).');
add('prop:crane_top', 'tower-crane-top-d8d9c9', 'p-crane-top', { load: false, jibSections: 6, slew: 0.5, wear: 0.1, paint: 'crane', lattice: 'crane', cab: 'cream', glass: 'navy', ballast: 'asphalt', machinery: 'ink', rope: 'ink', hook: 'coral', rebar: 'brickDeep', lamps: 'cloud', amber: 'sunshine' }, 'Crane jib and cab on top of the mast.');

export { variants };
