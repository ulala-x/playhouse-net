plugins {
    java
    `java-library`
}

group = "com.playhouse"
version = "0.1.0"

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

repositories {
    mavenCentral()
}

dependencies {
    // Netty for async networking
    implementation("io.netty:netty-all:4.1.107.Final")

    // LZ4 compression (optional)
    implementation("org.lz4:lz4-java:1.8.0")

    // Logging (SLF4J API)
    implementation("org.slf4j:slf4j-api:2.0.9")

    // Testing
    testImplementation("org.junit.jupiter:junit-jupiter:5.10.0")
    testImplementation("org.assertj:assertj-core:3.24.2")
    testRuntimeOnly("ch.qos.logback:logback-classic:1.4.11")
}

tasks.test {
    useJUnitPlatform()
}

tasks.withType<JavaCompile> {
    options.encoding = "UTF-8"
}
