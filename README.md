该程序对OSM道路网数据进行预处理，实现两个功能：
1.按照highway字段对低等级道路进行删除，仅保留高等级道路（highway="motorway", "motorway_link", "trunk", "trunk_link", "primary", "primary_link", "secondary", "secondary_link" ）;
2.对于连接道路（highway="motorway_link", "trunk_link", "primary_link", "secondary_link" ），如果删除该连接道路会导致主路“断头”，则保留，否则删除；

# Ver.1  2019.10.14  实现上述目标1，基本实现上述目标2，目标2的准确率还有待提升；