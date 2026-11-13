CREATE TABLE Brands (
    Name TEXT PRIMARY KEY,
    Description TEXT NOT NULL
);
CREATE TABLE Products (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    Brand TEXT NOT NULL REFERENCES Brands(Name),
    Category TEXT NOT NULL,
    Price INTEGER NOT NULL CHECK (Price > 0),
    OldPrice INTEGER CHECK (OldPrice > Price),
    Image TEXT NOT NULL,
    Color TEXT NOT NULL,
    Sizes TEXT NOT NULL CHECK (json_valid(Sizes)),
    IsNew INTEGER NOT NULL CHECK (IsNew IN (0, 1)),
    Popular INTEGER NOT NULL CHECK (Popular IN (0, 1))
);
CREATE TABLE Reviews (
    Id INTEGER PRIMARY KEY,
    ProductId INTEGER NOT NULL REFERENCES Products(Id),
    Name TEXT NOT NULL,
    Rating INTEGER NOT NULL CHECK (Rating BETWEEN 1 AND 5),
    Text TEXT NOT NULL,
    Date TEXT NOT NULL
);

INSERT INTO Brands VALUES
('Nike', 'Футболки, худи, брюки и одежда для тренировок'),
('Adidas', 'Футболки и легинсы');

-- Prices are stored in kopecks; these are the initial sample products.
INSERT INTO Products VALUES
(1, 'Худи Club Fleece', 'Nike', 'Худи', 649000, 799000, '/images/products/club-hoodie.webp', 'Серый', '["S","M","L","XL"]', 1, 1),
(2, 'Футболка Essentials', 'Adidas', 'Футболки', 299000, NULL, '/images/products/adidas-shirt.webp', 'Белый', '["XS","S","M","L","XL"]', 1, 1),
(3, 'Брюки Club Fleece', 'Nike', 'Брюки', 499000, 599000, '/images/products/club-joggers.webp', 'Серый', '["S","M","L","XL","XXL"]', 1, 1),
(4, 'Легинсы One Dri-FIT', 'Nike', 'Легинсы', 549000, NULL, '/images/products/one-leggings.webp', 'Серый', '["XS","S","M","L"]', 1, 1),
(5, 'Шорты Pro Dri-FIT', 'Nike', 'Шорты', 349000, 449000, '/images/products/pro-shorts.webp', 'Чёрный', '["S","M","L","XL"]', 1, 0),
(6, 'Куртка Windrunner', 'Nike', 'Куртки', 899000, 1099000, '/images/products/windrunner.webp', 'Светло-серый', '["S","M","L","XL"]', 1, 0),
(7, 'Легинсы Optimé Stash', 'Adidas', 'Легинсы', 629000, 749000, '/images/products/adidas-leggings.webp', 'Чёрный', '["XS","S","M","L"]', 0, 0),
(8, 'Футболка Sportswear Club', 'Nike', 'Футболки', 329000, NULL, '/images/products/club-shirt.webp', 'Белый', '["S","M","L","XL","XXL"]', 0, 0);

INSERT INTO Reviews VALUES
(1, 4, 'Анна', 5, 'Взяла для йоги. Ткань плотная, пояс не сползает. Размер M подошёл.', '2026-11-03'),
(2, 1, 'Михаил', 5, 'Мягкое худи, удобно и на прогулке, и после тренировки.', '2026-11-04'),
(3, 2, 'Дарья', 4, 'Хорошая базовая футболка. Крой свободный, для более плотной посадки взяла бы на размер меньше.', '2026-11-05');

PRAGMA user_version = 1;
