该程序对OSM道路网数据进行预处理，实现两个功能：
1.按照highway字段对低等级道路进行删除，仅保留高等级道路（highway="motorway", "motorway_link", "trunk", "trunk_link", "primary", "primary_link", "secondary", "secondary_link" ）;
2.对于连接道路（highway="motorway_link", "trunk_link", "primary_link", "secondary_link" ），如果删除该连接道路会导致主路“断头”，则保留，否则删除；

Ver.1  2019.10.14  实现上述目标1，基本实现上述目标2，目标2的准确率还有待提升；
Ver.2 2019.11.16 实现目标1和目标2，Link Stroke 的两端点分为四种类型：真断头，假断头，连接主要道路；
Ver.3 2019.11.19 以Tree方法代替Stroke方法；
Ver.4 2019.11.16 Tree方法和Stroke方法并存，已为成熟版本，可进行数据预处理；
Ver.5 20200926 最终版本；